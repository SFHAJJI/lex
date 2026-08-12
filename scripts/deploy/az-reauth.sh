# Re-exchange a FRESH GitHub OIDC assertion for Azure credentials.
#
# The assertion azure/login used is valid for five minutes. The candidate step runs far longer
# than that: run 31646994558 spent about three and a half minutes in the load generator alone,
# and the first Azure call after it was rejected outright:
#
#   ERROR: AADSTS700024: Client assertion is not within its valid time range.
#   Current time: 2026-08-12T22:41:09Z, assertion valid from 22:27:20, expiry 22:32:20
#
# Every functional gate had already passed and the load test had already reported zero failures
# at p95 439ms, so a healthy candidate was thrown away because its own verification outlasted
# the credential that authorised it. Polling the metrics for twelve minutes, which this workflow
# now does deliberately, makes that certain rather than likely.
#
# Requesting a new assertion is the documented remedy and needs nothing beyond the id-token:
# write permission the job already holds. The runner exposes a short-lived request token and URL
# for exactly this purpose. The assertion is never echoed, and neither is the request token.
az_reauth() {
  [ -n "${ACTIONS_ID_TOKEN_REQUEST_TOKEN:-}" ] && [ -n "${ACTIONS_ID_TOKEN_REQUEST_URL:-}" ] || {
    echo "::error::no OIDC request token available; the job needs id-token: write" >&2
    return 1
  }

  _azr_assertion=$(curl --fail --silent --show-error --retry 3 --retry-delay 2 \
    --connect-timeout 5 --max-time 30 \
    -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
    "${ACTIONS_ID_TOKEN_REQUEST_URL}&audience=api://AzureADTokenExchange" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["value"])') || {
    echo "::error::could not obtain a fresh OIDC assertion" >&2
    return 1
  }

  # --allow-no-subscriptions is deliberately absent: a login that silently lands without the
  # subscription would turn every later call into a confusing "resource not found".
  #
  # The assertion is cleared on BOTH paths and before anything else can run. Unsetting it only
  # after a successful login would leave a live federated token in the shell for the remainder
  # of the step on exactly the failure path where the step is least likely to end quickly.
  if az login --service-principal \
      --username "$AZURE_CLIENT_ID" \
      --tenant "$AZURE_TENANT_ID" \
      --federated-token "$_azr_assertion" \
      --output none; then
    unset _azr_assertion
  else
    unset _azr_assertion
    echo "::error::re-authentication failed" >&2
    return 1
  fi

  az account set --subscription "$AZURE_SUBSCRIPTION_ID" --output none
  # stderr, not stdout: this helper must stay safe to call inside a command substitution.
  echo "re-authenticated with a fresh OIDC assertion" >&2
}
