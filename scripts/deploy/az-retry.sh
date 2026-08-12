# Retry an Azure write that lost a race against another write on the same entity.
#
# Azure Container Apps serialises writes to one app. This workflow issues several back to
# back, and a write that arrives while the previous one is still propagating is rejected
# outright:
#
#   ERROR: (ConflictingConcurrentWriteNotAllowed) The operation was interrupted by a
#   conflicting concurrent write on the same entity. Please retry later.
#
# Run 31633793807 died exactly there, on "registry set" immediately after "identity assign",
# after the image had already been built. The whole deploy was lost to a five second timing
# window.
#
# Retrying is the correct response here rather than a workaround, for three reasons. The
# rejected operation was never applied, so there is no partial state to reason about. Azure
# names the condition as retryable in the error itself. And every call wrapped below is
# idempotent: assigning an identity that is already assigned, setting a registry to the value
# it already holds, or patching a template to the shape it already has are all no-ops.
#
# Do NOT wrap a call that is not idempotent. This helper exists to absorb a lost race, not to
# paper over a failure whose cause is unknown: any error that is not a write conflict is
# returned immediately and unchanged, so a genuine RBAC or validation failure still stops the
# deploy on its first attempt rather than being retried into a five minute delay.

az_write() {
  attempt=1
  max_attempts=6
  delay=10

  while :; do
    if output=$("$@" 2>&1); then
      [ -n "$output" ] && printf '%s\n' "$output"
      return 0
    fi

    case "$output" in
      *ConflictingConcurrentWriteNotAllowed*|*'conflicting concurrent write'*)
        if [ "$attempt" -ge "$max_attempts" ]; then
          printf '%s\n' "$output" >&2
          echo "::error::write still conflicting after $max_attempts attempts: $*" >&2
          return 1
        fi
        echo "conflicting concurrent write, attempt $attempt of $max_attempts, retrying in ${delay}s" >&2
        sleep "$delay"
        attempt=$((attempt + 1))
        delay=$((delay * 2))
        ;;
      *)
        printf '%s\n' "$output" >&2
        return 1
        ;;
    esac
  done
}
