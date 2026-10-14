# The private copy's address list (the plan's item 0.5): the ranges a deployment names, and the check of who ingress
# admits that deploy.ps1 reads back after each deployment and after the owner's opening. Pure functions, dot-sourced by
# deploy.ps1, so web/test/deploy-kit.test.mjs runs them under pwsh with made-up answers in place of the Azure CLI's.

<#
.SYNOPSIS
The ranges -AllowedCidr names, one per element: `pwsh -File` hands `-AllowedCidr a,b` over as one string, so each value
is split on commas (review of #960: a second range was refused in the documented form).
#>
function Get-AllowedCidrs([string[]] $Values) {
    @($Values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

<#
.SYNOPSIS
Why a range cannot stand in the private copy's list, or $null: an IPv4 address with a prefix of 24 to 32 bits, the
operator's address or a reviewer's network, never a block that admits the public (review of #960: 0.0.0.0/0 deployed
as "the site stays private").
#>
function Get-AllowedCidrFailure([string] $Cidr) {
    if ($Cidr -notmatch '^((25[0-5]|2[0-4][0-9]|1?[0-9]?[0-9])\.){3}(25[0-5]|2[0-4][0-9]|1?[0-9]?[0-9])/([0-9]|[12][0-9]|3[0-2])$') {
        return "'$Cidr' is not an IPv4 address range such as 203.0.113.7/32"
    }
    if ([int]($Cidr -split '/')[1] -lt 24) {
        return "'$Cidr' admits more than 256 addresses; the private copy admits an address or a small network (/24 to /32)"
    }
    return $null
}

<#
.SYNOPSIS
Why who ingress admits is not what was asked, or $null. RulesJson is the app's
`properties.configuration.ingress.ipSecurityRestrictions`, as `az containerapp show ... -o json` prints it ("null" or
"[]" when there is none). With -Public nothing may be restricted; otherwise the rules must be exactly the Allowed ranges,
each an Allow rule.
#>
function Get-AdmittedFailure([string] $RulesJson, [string[]] $Allowed, [bool] $Public) {
    $rules = @(if ([string]::IsNullOrWhiteSpace($RulesJson)) { } else { $RulesJson | ConvertFrom-Json }) | Where-Object { $_ }
    $ranges = @($rules | ForEach-Object { $_.ipAddressRange } | Sort-Object)
    if ($Public) {
        if ($ranges.Count -ne 0) { return "ingress still admits only $($ranges -join ', ')" }
        return $null
    }
    $actions = @($rules | ForEach-Object { $_.action } | Sort-Object -Unique)
    $expected = @($Allowed | Sort-Object)
    if ($ranges.Count -eq 0) { return "ingress admits everyone" }
    if ($actions.Count -ne 1 -or $actions[0] -ne 'Allow' -or (Compare-Object $ranges $expected)) {
        return "ingress admits $($ranges -join ', ') ($($actions -join ', ')), not exactly $($expected -join ', ')"
    }
    return $null
}
