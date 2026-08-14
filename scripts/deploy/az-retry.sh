# Retry an idempotent Azure operation after a bounded transient failure.
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
# The retry set is explicit: authentication expiry, write conflict, service throttling and
# transient transport/service failures. Any other error is returned immediately and unchanged,
# so a genuine RBAC or validation failure stops on its first attempt.
#
# Every variable below is prefixed, because this file is sourced into the caller's shell and
# the calling steps use short names like "attempt" for their own loops.

az_retry() {
  _azt_attempt=1
  _azt_max=${AZ_RETRY_MAX_ATTEMPTS:-6}
  _azt_delay=${AZ_RETRY_BASE_DELAY:-10}

  while :; do
    if _azt_output=$("$@" 2>&1); then
      [ -n "$_azt_output" ] && printf '%s\n' "$_azt_output"
      return 0
    fi

    case "$_azt_output" in
      *AADSTS700024*|*ExpiredAuthenticationToken*)
        if command -v az_reauth >/dev/null 2>&1; then
          az_reauth || return 1
        else
          printf '%s\n' "$_azt_output" >&2
          return 1
        fi
        ;;
      *ConflictingConcurrentWriteNotAllowed*|*'conflicting concurrent write'*|*TooManyRequests*|*ServiceUnavailable*|*InternalServerError*|*GatewayTimeout*|*'Connection reset'*|*'timed out'*)
        ;;
      *)
        printf '%s\n' "$_azt_output" >&2
        return 1
        ;;
    esac

    if [ "$_azt_attempt" -ge "$_azt_max" ]; then
      printf '%s\n' "$_azt_output" >&2
      echo "::error::Azure operation still transiently failing after $_azt_max attempts: $*" >&2
      return 1
    fi
    echo "transient Azure failure, attempt $_azt_attempt of $_azt_max, retrying in ${_azt_delay}s" >&2
    sleep "$_azt_delay"
    _azt_attempt=$((_azt_attempt + 1))
    _azt_delay=$((_azt_delay * 2))
  done
}

az_write() {
  az_retry "$@"
}
