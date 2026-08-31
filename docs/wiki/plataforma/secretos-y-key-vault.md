<!-- wiki-meta
sources:
  - src/core/Axxon.Eip.Core/Configuration/**
  - infra/modules/keyvault.bicep
  - infra/scripts/**
last_reviewed: 2026-08-31
-->

# Secretos y Key Vault

Todos los secretos viven en Azure Key Vault (un vault por ambiente). Nada de secretos
en Application Settings planos ni en el repo.

### Configuración

1. Crear el Key Vault (ej: `kv-chacomer-eip-{env}`) con **RBAC authorization**.
2. Asignar a la Managed Identity de cada Function App el rol **Key Vault Secrets User**:

```bash
az role assignment create \
  --assignee <PRINCIPAL_ID_DE_LA_MI> \
  --role "Key Vault Secrets User" \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.KeyVault/vaults/kv-chacomer-eip-prod
```

3. Agregar el Application Setting `KeyVaultUri = https://kv-chacomer-eip-{env}.vault.azure.net/`
   en cada Function App. Con eso `AddEipCore()` carga el vault como configuration provider:
   cada secret se expone como clave de configuración con su mismo nombre, y **pisa** cualquier
   App Setting duplicado.

### Convención de nombres de secrets

| Clave de configuración | Usada por | Descripción |
|---|---|---|
| `DataverseClientSecret` | todas (DESA/INTE) | Client Secret del App Registration de Dataverse |
| `FoClientSecret` | customers, customergroups, products (DESA/INTE) | Client Secret del App Registration de F&O |
| `SetApiKey` | contacts, fiscal | API Key de la SET Paraguay |
| `GraphClientSecret` | ticketatencion (DESA/INTE) | Client Secret del App Registration de Microsoft Graph. Hoy es el mismo de Dataverse y **no se duplica en el vault**: el param `graphClientSecretName` emite la indirección para resolverlo desde `DataverseClientSecret` |

En producción las conexiones a Dataverse/F&O usan Managed Identity: no hay secreto que guardar.
Los secrets `*ClientSecret` solo existen en los vaults de DESA/INTE.

#### Cuando el secret del vault se llama distinto

Por defecto el secret del Key Vault se llama igual que la clave de arriba. Los vaults legacy
no siguen esa convención: nombran los secrets por app registration y ambiente. Para eso está
la indirección `{clave}Name` — un Application Setting con el nombre real del secret:

| Application Setting | Valor en INTE |
|---|---|
| `KeyVaultUri` | `https://keyvaultinte.vault.azure.net/` |
| `DataverseClientSecretName` | `SecretNextGenDynamics365Inte` |
| `FoClientSecretName` | `SecretNextGenDynamics365Inte` |

Con eso, `AddEipDataverse()` resuelve `DataverseClientSecret` leyendo el secret
`SecretNextGenDynamics365Inte` del vault. Sin `{clave}Name`, el comportamiento es el de
siempre: se busca el secret con el nombre de la clave.

Si `{clave}Name` apunta a un secret que no resuelve, **el host no levanta**: es intencional.
Devolver `null` dejaría `UseClientSecretAuth` en `false` y la app caería en silencio a Managed
Identity, fallando más adelante con un error que no menciona el secreto mal configurado.

Ver [`EipSecretResolver`](../../../src/core/Axxon.Eip.Core/Configuration/EipSecretResolver.cs).

### Triggers y bindings (resueltos por el host, no por el worker)

Los settings que consume el **host** de Functions (ej: `ServiceBusConnection` de los triggers,
binding expressions `%...%`) NO pasan por el configuration provider del worker. Para esos:

- **Producción:** Managed Identity — `ServiceBusConnection__fullyQualifiedNamespace` (sin secreto).
- **DESA con connection string:** Key Vault reference en el Application Setting:
  `@Microsoft.KeyVault(SecretUri=https://kv-chacomer-eip-desa.vault.azure.net/secrets/ServiceBusConnection/)`

### Desarrollo local

Dos opciones:
- `az login` + `KeyVaultUri` en `local.settings.json` → lee los secrets del vault. Con el vault
  de INTE, agregando también los `*SecretName`:

  ```json
  {
    "Values": {
      "KeyVaultUri": "https://keyvaultinte.vault.azure.net/",
      "DataverseClientSecretName": "SecretNextGenDynamics365Inte",
      "FoClientSecretName": "SecretNextGenDynamics365Inte"
    }
  }
  ```

  `DefaultAzureCredential` usa la sesión de `az login`, así que el usuario necesita
  **Key Vault Secrets User** sobre el vault.
- Sin `KeyVaultUri` → los valores se toman de `local.settings.json` como siempre.

## Secretos en Key Vault (paso posterior al deploy)

Bicep **no** crea valores de secretos (no se exponen en params ni en el state).
Se cargan una vez con `az keyvault secret set`. En producción, Dataverse y F&O
usan Managed Identity, así que los `*ClientSecret` solo existen en el vault de DESA/INTE.

```bash
VAULT=kv-chacomer-eip-test

# API Key de la SET Paraguay (contacts + fiscal) — requerido en todos los ambientes
az keyvault secret set --vault-name $VAULT --name SetApiKey --value "<api-key>"

# Token de Thinkchat (app thinkchat). El nombre del secret es el que ya usa INTE.
az keyvault secret set --vault-name $VAULT --name secretThinkChat --value "<token>"

# Client secrets (solo ambientes sin Managed Identity contra Dataverse/F&O)
az keyvault secret set --vault-name $VAULT --name DataverseClientSecret --value "<secret>"
az keyvault secret set --vault-name $VAULT --name FoClientSecret --value "<secret>"
```

El nombre del secret coincide con la clave de configuración que lee el código
(`AddEipKeyVault` monta el vault como configuration provider), así que **no hace falta
ningún app setting**: alcanza con `KeyVaultUri`, que ya declara el template.

Ese es el motivo de usar los nombres canónicos y no la indirección `{clave}Name`: un
`DataverseClientSecretName` puesto a mano lo borra el próximo deployment, porque este
template declara la colección completa de `appSettings`. Con el nombre canónico el
cableado vive en el vault, que el deployment no toca.

> La indirección sigue existiendo en el código (`EipSecretResolver`) y se usa en INTE
> mientras esas apps estén fuera del Bicep. Es transitoria, no el patrón a seguir.

### Los `*ClientId` van en el template, no a mano

`DataverseClientId`, `FoClientId` y `FoTenantId` **no son secretos** pero sí son necesarios:
sin ellos `UseClientSecretAuth` queda en false y la app cae a Managed Identity en silencio,
fallando recién al primer llamado a Dataverse o F&O. Se declaran con los params
`dataverseClientId` / `foClientId` / `foTenantId`, vacíos por default (= Managed Identity,
el estado deseado). `products` va a propósito sin ellos: ya corre por MI.

### Dos vaults por resource group

Cada RG tiene el vault del template (`kv-chacomer-eip-{env}`) y uno legacy hecho a mano
(`keyvaultinte`, `keyvaultchacomertest`). **El que queda es el del template**: tiene purge
protection —los legacy no, y eso no se arregla sin recrear el vault— y está versionado acá.

| | Consumidores hoy | Qué hacer |
|---|---|---|
| `kv-chacomer-eip-test` | las 5 Function Apps de TEST | es el bueno |
| `keyvaultchacomertest` | ninguno | se puede borrar |
| `keyvaultinte` | 4 apps de la EiP + `fa-axxonticketatencion-inte` + SP `NextGenInte` | migrar |
| `kv-chacomer-eip-inte` | ninguno todavía | destino de INTE |

Migrar INTE no es sólo cambiar `KeyVaultUri`: hay que dar `Key Vault Secrets User` sobre el
vault nuevo a cada MI y **coordinar con el dueño de `fa-axxonticketatencion-inte`**, que no
vive en este repo.

