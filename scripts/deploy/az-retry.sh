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
# names the condition as retryable in the error itself. And every call wrapped by this helper
# is idempotent: assigning an identity that is already assigned, setting a registry to the
# value it already holds, or patching a template to the shape it already has are all no-ops.
#
# Do NOT wrap a call that is not idempotent, and do NOT wrap a call that already sits in a
# loop which verifies the end state. The two revision deactivations in deploy.yml are
# deliberately left unwrapped for that second reason: their own loop retries and then reads
# properties.active back, which is a stronger check than any return code, and nesting an
# exponential backoff inside it would multiply a five second race into minutes.
#
# Any error that is not a write conflict is returned immediately and unchanged, so a genuine
# RBAC or validation failure still stops the deploy on its first attempt rather than being
# buried under minutes of backoff.
#
# Every variable below is prefixed, because this file is sourced into the caller's shell and
# the calling steps use short names like "attempt" for their own loops.

az_write() {
  _azw_attempt=1
  _azw_max=${AZ_WRITE_MAX_ATTEMPTS:-6}
  _azw_delay=${AZ_WRITE_BASE_DELAY:-10}

  while :; do
    if _azw_output=$("$@" 2>&1); then
      [ -n "$_azw_output" ] && printf '%s\n' "$_azw_output"
      return 0
    fi

    case "$_azw_output" in
      *ConflictingConcurrentWriteNotAllowed*|*'conflicting concurrent write'*)
        if [ "$_azw_attempt" -ge "$_azw_max" ]; then
          printf '%s\n' "$_azw_output" >&2
          echo "::error::write still conflicting after $_azw_max attempts: $*" >&2
          return 1
        fi
        echo "conflicting concurrent write, attempt $_azw_attempt of $_azw_max, retrying in ${_azw_delay}s" >&2
        sleep "$_azw_delay"
        _azw_attempt=$((_azw_attempt + 1))
        _azw_delay=$((_azw_delay * 2))
        ;;
      *)
        printf '%s\n' "$_azw_output" >&2
        return 1
        ;;
    esac
  done
}
