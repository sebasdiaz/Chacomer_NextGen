#Requires -Version 7.0
<#
.SYNOPSIS
    Migra las Function Apps de INTE a autenticacion por Key Vault + Managed Identity.

.DESCRIPTION
    Las apps de INTE fueron creadas a mano y quedan fuera del Bicep
    (inte.bicepparam va con deployFunctionApps = false), asi que su configuracion
    no se puede versionar en un template. Este script hace ese cutover de forma
    reproducible e idempotente: se puede correr N veces y converge al mismo estado.

    Por cada app:
      1. Habilita System-Assigned Managed Identity (no-op si ya la tiene).
      2. Le da "Key Vault Secrets User" sobre el vault (no-op si ya lo tiene).
      3. Setea KeyVaultUri y los *SecretName que apuntan al secret real del vault.
      4. Unifica DataverseClientId / FoClientId en el app registration indicado.
      5. Borra los client secrets en texto plano — y SOLO si 1-4 salieron bien.

    El orden importa: si el borrado del paso 5 corriera antes de que la app pueda
    leer el vault, queda sin credenciales y se cae.

.PARAMETER ResourceGroup
    Resource group de las Function Apps. Default: DataverseINTE.

.PARAMETER VaultName
    Key Vault que tiene el secret. Default: keyvaultinte.

.PARAMETER SecretName
    Nombre del secret con el client secret del app registration.
    Default: SecretNextGenDynamics365Inte.

.PARAMETER ClientId
    App registration al que pertenece el secret. Se escribe en DataverseClientId
    y FoClientId de todas las apps, unificandolas.
    Default: 145fd64d-3deb-46eb-9f58-736d1ff46a3e (NextGen_Dynamics365_Inte).

.PARAMETER SetApiKeySecretName
    Nombre del secret con el API Key de la SET, si ya se cargo en el vault.
    Sin este parametro, SetApiKey de fa-axxoncontacts-inte se deja como esta.

.PARAMETER Apps
    Subconjunto de apps a migrar. Sin este parametro, las cuatro.

.PARAMETER SkipClientIdUnification
    No toca DataverseClientId / FoClientId. Usar si el app registration destino
    todavia no esta dado de alta como Application User en Dataverse o como usuario
    S2S en F&O para alguna de las apps.

.PARAMETER RemovePlainSecrets
    Borra los settings planos (DataverseClientSecret, FoClientSecret, SetApiKey).
    Aparte a proposito: conviene correr el script sin esto, validar que la app
    levanta y funciona leyendo del vault, y recien despues borrar.

.PARAMETER RolePropagationTimeoutSeconds
    Cuanto esperar a que propague un role assignment recien creado antes de setear
    KeyVaultUri. Default: 180. Si se agota, la app se saltea y basta con volver a
    correr el script.

.PARAMETER WhatIf
    Muestra lo que haria sin ejecutar nada.

.EXAMPLE
    # 1. Dry run
    ./Set-InteKeyVaultAuth.ps1 -WhatIf

    # 2. Una sola app, sin borrar nada todavia
    ./Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte

    # 3. Validada la app, se borran los secretos planos
    ./Set-InteKeyVaultAuth.ps1 -Apps fa-axxoncontacts-inte -RemovePlainSecrets

.NOTES
    Requiere az CLI con sesion iniciada en la suscripcion AZURE_DYNAMICS
    (09592883-de3a-4c93-944c-222b3c88e832) y permisos para asignar roles.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]   $ResourceGroup        = 'DataverseINTE',
    [string]   $VaultName            = 'keyvaultinte',
    [string]   $SecretName           = 'SecretNextGenDynamics365Inte',
    [string]   $ClientId             = '145fd64d-3deb-46eb-9f58-736d1ff46a3e',
    [string]   $SetApiKeySecretName,
    [string[]] $Apps,
    [switch]   $SkipClientIdUnification,
    [switch]   $RemovePlainSecrets,
    [int]      $RolePropagationTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

# Que consume cada app. contacts no habla con F&O directo (publica en Service Bus),
# asi que no necesita FoClientSecret.
$appProfiles = [ordered]@{
    'fa-axxoncontacts-inte'  = @{ Dataverse = $true; FoOData = $false; SetApi = $true  }
    'fa-axxoncustomers-inte' = @{ Dataverse = $true; FoOData = $true;  SetApi = $false }
    'fa-axxonproducts-inte'  = @{ Dataverse = $true; FoOData = $true;  SetApi = $false }
    # Sin sufijo -inte: se llama asi en Azure. Ver el punto 4 del cutover en infra/README.md.
    'fa-axxoncustomergroup'  = @{ Dataverse = $true; FoOData = $true;  SetApi = $false }
}

if ($Apps) {
    $desconocidas = $Apps | Where-Object { -not $appProfiles.Contains($_) }
    if ($desconocidas) {
        throw "App(s) no reconocida(s): $($desconocidas -join ', '). Validas: $($appProfiles.Keys -join ', ')"
    }
    $targetApps = $Apps
} else {
    $targetApps = @($appProfiles.Keys)
}

function Invoke-Az {
    param([string[]] $Arguments, [switch] $AllowFailure)

    $output = & az @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "az $($Arguments -join ' ') fallo:`n$output"
    }
    return $output
}

# `-o tsv` de un valor ausente (ej: identity.principalId de una app sin MI) devuelve
# vacio, y ahi Invoke-Az entrega $null. Colapsar a string vacio evita el
# "cannot call a method on a null-valued expression" en cada .Trim() del script.
function Get-AzText {
    param([string[]] $Arguments, [switch] $AllowFailure)

    $output = Invoke-Az -Arguments $Arguments -AllowFailure:$AllowFailure
    return ([string]::Join('', @($output))).Trim()
}

Write-Host "Vault:  $VaultName / secret '$SecretName'"
Write-Host "Client: $ClientId"
Write-Host "Apps:   $($targetApps -join ', ')`n"

# Chequeo previo: si el secret no esta, todo lo que sigue deja las apps sin credenciales.
$secretOk = Get-AzText @('keyvault', 'secret', 'show',
    '--vault-name', $VaultName, '--name', $SecretName,
    '--query', 'attributes.enabled', '-o', 'tsv') -AllowFailure

if ($secretOk -ne 'true') {
    throw "El secret '$SecretName' no existe o esta deshabilitado en '$VaultName'. " +
          "Cargarlo con: az keyvault secret set --vault-name $VaultName --name $SecretName --value <secret>"
}

$vaultUri = Get-AzText @('keyvault', 'show', '--name', $VaultName,
    '--query', 'properties.vaultUri', '-o', 'tsv')

$vaultId = Get-AzText @('keyvault', 'show', '--name', $VaultName,
    '--query', 'id', '-o', 'tsv')

foreach ($app in $targetApps) {
    $appProfile = $appProfiles[$app]
    Write-Host "── $app ──────────────────────────────────"

    # ---- 1. Managed Identity ----
    $principalId = Get-AzText @('functionapp', 'show', '-g', $ResourceGroup, '-n', $app,
        '--query', 'identity.principalId', '-o', 'tsv')

    if ([string]::IsNullOrWhiteSpace($principalId) -or $principalId -eq 'None') {
        if ($PSCmdlet.ShouldProcess($app, 'habilitar System-Assigned Managed Identity')) {
            $principalId = Get-AzText @('functionapp', 'identity', 'assign',
                '-g', $ResourceGroup, '-n', $app,
                '--query', 'principalId', '-o', 'tsv')
            Write-Host "  MI habilitada: $principalId"
        } else {
            # En -WhatIf no hay principalId real. Se sigue igual para que el dry run
            # muestre los app settings de TODAS las apps, no solo las que ya tienen MI.
            $principalId = ''
        }
    } else {
        Write-Host "  MI ya existente: $principalId"
    }

    # ---- 2. Key Vault Secrets User ----
    if ([string]::IsNullOrWhiteSpace($principalId)) {
        Write-Host '  Rol "Key Vault Secrets User": se asignaria a la MI recien creada'
    } else {
        # Se lista con --all y se filtra el scope aca: `az role assignment list --scope`
        # devuelve MissingSubscription cuando la CLI no tiene el contexto de suscripcion
        # fijado, y el filtro en PowerShell ademas es case-insensitive (los resource IDs
        # de ARM no lo son de forma consistente).
        $scopes = @(Invoke-Az @('role', 'assignment', 'list',
            '--assignee', $principalId, '--all',
            '--query', "[?roleDefinitionName=='Key Vault Secrets User'].scope",
            '-o', 'tsv') -AllowFailure)

        $yaTieneRol = $scopes | Where-Object { $_ -and $_.Trim() -ieq $vaultId }

        if ($yaTieneRol) {
            Write-Host '  Rol "Key Vault Secrets User" ya asignado'
        } elseif ($PSCmdlet.ShouldProcess("$app -> $VaultName", 'asignar "Key Vault Secrets User"')) {
            Invoke-Az @('role', 'assignment', 'create',
                '--assignee-object-id', $principalId,
                '--assignee-principal-type', 'ServicePrincipal',
                '--role', 'Key Vault Secrets User',
                '--scope', $vaultId) | Out-Null
            Write-Host '  Rol "Key Vault Secrets User" asignado'

            # Esperar la propagacion ANTES de setear KeyVaultUri. Si la app arranca
            # con el vault configurado y el rol todavia no propago, AddAzureKeyVault
            # tira en el arranque y la app queda caida hasta el proximo cold start.
            $deadline = (Get-Date).AddSeconds($RolePropagationTimeoutSeconds)
            $propagado = $false

            while (-not $propagado -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 10
                $visible = @(Invoke-Az @('role', 'assignment', 'list',
                    '--assignee', $principalId, '--all',
                    '--query', "[?roleDefinitionName=='Key Vault Secrets User'].scope",
                    '-o', 'tsv') -AllowFailure)
                $propagado = [bool]($visible | Where-Object { $_ -and $_.Trim() -ieq $vaultId })
            }

            if ($propagado) {
                Write-Host '  Rol visible en ARM; margen extra para el plano de datos del vault'
                Start-Sleep -Seconds 30
            } else {
                Write-Warning ("  El rol no se hizo visible en {0}s. Se saltea esta app: volver a " +
                    "correr el script mas tarde para setear los app settings." -f $RolePropagationTimeoutSeconds)
                continue
            }
        }
    }

    # ---- 3 y 4. App settings ----
    $settings = @("KeyVaultUri=$vaultUri")

    if ($appProfile.Dataverse) {
        $settings += "DataverseClientSecretName=$SecretName"
        if (-not $SkipClientIdUnification) { $settings += "DataverseClientId=$ClientId" }
    }
    if ($appProfile.FoOData) {
        $settings += "FoClientSecretName=$SecretName"
        if (-not $SkipClientIdUnification) { $settings += "FoClientId=$ClientId" }
    }
    if ($appProfile.SetApi -and $SetApiKeySecretName) {
        $settings += "SetApiKeyName=$SetApiKeySecretName"
    }

    if ($PSCmdlet.ShouldProcess($app, "setear: $($settings -join ', ')")) {
        Invoke-Az (@('functionapp', 'config', 'appsettings', 'set',
            '-g', $ResourceGroup, '-n', $app, '--settings') + $settings + @('-o', 'none')) | Out-Null
        Write-Host "  App settings seteados ($($settings.Count))"
    }

    # ---- 5. Borrado de los secretos planos ----
    if (-not $RemovePlainSecrets) {
        Write-Host '  Secretos planos: intactos (usar -RemovePlainSecrets tras validar la app)'
        continue
    }

    $aBorrar = @()
    if ($appProfile.Dataverse) { $aBorrar += 'DataverseClientSecret' }
    if ($appProfile.FoOData)   { $aBorrar += 'FoClientSecret' }
    if ($appProfile.SetApi -and $SetApiKeySecretName) { $aBorrar += 'SetApiKey' }

    $existentes = @(Invoke-Az @('functionapp', 'config', 'appsettings', 'list',
        '-g', $ResourceGroup, '-n', $app, '--query', '[].name', '-o', 'tsv') |
        Where-Object { $_ -in $aBorrar })

    if (-not $existentes) {
        Write-Host '  Secretos planos: ya no quedan'
    } elseif ($PSCmdlet.ShouldProcess($app, "BORRAR settings planos: $($existentes -join ', ')")) {
        Invoke-Az (@('functionapp', 'config', 'appsettings', 'delete',
            '-g', $ResourceGroup, '-n', $app, '--setting-names') + $existentes + @('-o', 'none')) | Out-Null
        Write-Host "  Secretos planos borrados: $($existentes -join ', ')"
    }
}

Write-Host "`nListo. Verificar que no quede ningun secreto plano:"
Write-Host "  az functionapp config appsettings list -g $ResourceGroup -n <app> --query `"[].name`" -o tsv"
