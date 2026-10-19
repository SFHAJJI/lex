<#
.SYNOPSIS
Binds the site's name (law.soufien.lu) to the container app with a free managed certificate, once public DNS points the
name at the app.

.DESCRIPTION
The owner decided on 2026-10-14 that the site keeps V2's address, law.soufien.lu (O14). Binding needs two records where
the domain's DNS is served: `law` as a CNAME to the app's default host, and `asuid.law` as a TXT holding the app's
custom-domain verification id. This script:
1. reads the app's default host and verification id;
2. when the zone is an Azure DNS zone in the resource group (-Zone), sets the two records there (an existing record of
   another value is replaced, and the change is shown);
3. checks that public DNS (a public resolver, never the zone's own servers) answers the CNAME with the app's host and the
   TXT with the verification id, and stops, changing nothing in the app, if it does not (the registrar's name servers must
   name the zone's);
4. adds the hostname to the app and binds it with a managed certificate (validated by the CNAME), then reads back that
   the binding is SniEnabled with a certificate.
Without -Apply it only plans: it reads and checks, and prints what it would change. It never opens the site to the
public: that is deploy.ps1 -Open, the owner's opening. Later deployments keep the binding: deploy.ps1 reads the app's
custom domains back and passes them to the template.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Subscription,
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [string] $AppName = 'ca-lex-v3',
    [Parameter(Mandatory)] [string] $Environment,
    [string] $Hostname = 'law.soufien.lu',
    [string] $Zone = '',
    [string] $Resolver = '1.1.1.1',
    [switch] $Apply
)
$ErrorActionPreference = 'Stop'
function Step([string] $what) { Write-Host "== $what" }

az account set --subscription $Subscription
$app = az containerapp show -g $ResourceGroup -n $AppName --query "{fqdn:properties.configuration.ingress.fqdn, verification:properties.customDomainVerificationId, domains:properties.configuration.ingress.customDomains}" -o json | ConvertFrom-Json
if (-not $app.fqdn -or -not $app.verification) { throw "The app $AppName names no default host or verification id." }
$label = if ($Zone) { $Hostname.Substring(0, $Hostname.Length - $Zone.Length).TrimEnd('.') } else { $Hostname.Split('.')[0] }
Step "the app answers at $($app.fqdn); $Hostname must be a CNAME to it, and asuid.$label a TXT holding its verification id"

if ($Zone) {
    Step "the two records in the Azure DNS zone $Zone"
    $cname = az network dns record-set cname show -g $ResourceGroup -z $Zone -n $label --query "CNAMERecord.cname" -o tsv 2>$null
    $txt = az network dns record-set txt show -g $ResourceGroup -z $Zone -n "asuid.$label" --query "TXTRecords[0].value[0]" -o tsv 2>$null
    Write-Host "  $label CNAME now: $(if ($cname) { $cname } else { 'none' }); asuid.$label TXT now: $(if ($txt) { 'set' } else { 'none' })"
    if ($cname -ne $app.fqdn) {
        Write-Host "  > az network dns record-set cname set-record -g $ResourceGroup -z $Zone -n $label -c $($app.fqdn)"
        if ($Apply) { az network dns record-set cname set-record -g $ResourceGroup -z $Zone -n $label -c $app.fqdn -o none }
    }
    if ($txt -ne $app.verification) {
        Write-Host "  > az network dns record-set txt add-record -g $ResourceGroup -z $Zone -n asuid.$label -v <the app's verification id>"
        if ($Apply) {
            if ($txt) { az network dns record-set txt delete -g $ResourceGroup -z $Zone -n "asuid.$label" --yes -o none }
            az network dns record-set txt add-record -g $ResourceGroup -z $Zone -n "asuid.$label" -v $app.verification -o none
        }
    }
}

Step "public DNS, asked of $Resolver"
$seenCname = $null; $seenTxt = $null
try { $seenCname = (Resolve-DnsName $Hostname -Type CNAME -Server $Resolver -DnsOnly -ErrorAction Stop | Where-Object { $_.Type -eq 'CNAME' } | Select-Object -First 1).NameHost } catch { }
try { $seenTxt = (Resolve-DnsName "asuid.$Hostname" -Type TXT -Server $Resolver -DnsOnly -ErrorAction Stop | Where-Object { $_.Type -eq 'TXT' } | Select-Object -First 1).Strings -join '' } catch { }
Write-Host "  $Hostname CNAME: $(if ($seenCname) { $seenCname } else { 'no answer' }); asuid.$Hostname TXT: $(if ($seenTxt -eq $app.verification) { 'the verification id' } elseif ($seenTxt) { 'another value' } else { 'no answer' })"
if ($seenCname -ne $app.fqdn -or $seenTxt -ne $app.verification) {
    throw "Public DNS does not yet answer $Hostname as a CNAME to $($app.fqdn) with its verification TXT; nothing was bound. Check that the registrar's name servers name the zone's, then run again."
}

Step "bind $Hostname to $AppName with a managed certificate"
$bound = @($app.domains | Where-Object { $_.name -eq $Hostname })
if ($bound.Count -and $bound[0].bindingType -eq 'SniEnabled' -and $bound[0].certificateId) {
    Write-Host "  already bound, SniEnabled, with a certificate"
    exit 0
}
Write-Host "  > az containerapp hostname add -g $ResourceGroup -n $AppName --hostname $Hostname"
Write-Host "  > az containerapp hostname bind -g $ResourceGroup -n $AppName --hostname $Hostname --environment $Environment --validation-method CNAME"
if (-not $Apply) { Write-Host "== planned only: run again with -Apply to bind"; exit 0 }
if (-not $bound.Count) { az containerapp hostname add -g $ResourceGroup -n $AppName --hostname $Hostname -o none }
az containerapp hostname bind -g $ResourceGroup -n $AppName --hostname $Hostname --environment $Environment --validation-method CNAME -o none
$after = az containerapp show -g $ResourceGroup -n $AppName --query "properties.configuration.ingress.customDomains[?name=='$Hostname'] | [0]" -o json | ConvertFrom-Json
if ($after.bindingType -ne 'SniEnabled' -or -not $after.certificateId) { throw "$Hostname is not bound with a certificate (binding $($after.bindingType))." }
Step "$Hostname is bound, SniEnabled, with a managed certificate; the site stays as private as its address rules say"
