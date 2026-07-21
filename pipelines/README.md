# Pipelines — Enterprise Integration Platform (EiP)

Modelo de entrega: **Axxon mantiene el repo y entrega releases**. Al cierre de
sprint se taggea una release y se espeja al Azure DevOps del cliente, donde el
**pipeline de release** actualiza el ambiente de test (y, con aprobación, UAT/PROD).

## Archivos

| Archivo | Rol | Trigger |
|---|---|---|
| `azure-pipelines-release.yml` | **Release completo**: infra + las 4 apps a un ambiente | tag `v*` (auto) / manual |
| `azure-pipelines-infra.yml` | Solo infra (targeted) | manual |
| `azure-pipelines-{app}.yml` | Redeploy de una sola app (targeted) | manual |
| `templates/infra-deploy.yml` | Template: what-if + deploy del Bicep | — |
| `templates/functionapp-build-deploy.yml` | Template: build + deploy de una Function App | — |
| `vars/env-config.yml` | Config por ambiente (resource group) | — |

## Multi-ambiente

Todos los pipelines toman el parámetro **`environmentName`** (`inte` \| `uat` \| `prod`;
`inte` es el entorno de integración/test). A partir de él se derivan:

- **Service connection**: por convención `sc-chacomer-eip-{env}`.
- **Function Apps**: `fa-axxon{dominio}-{env}`.
- **`.bicepparam`**: `infra/environments/{env}.bicepparam`.
- **Resource group**: desde `vars/env-config.yml` (no es derivable — nombres no uniformes).
- **Environments de Azure DevOps** (gates): `{env}` para apps, `{env}-infra` para infra.

## Flujo de release (cierre de sprint)

```
Sprint (repo Axxon) → PR → main → tag vX.Y.Z
        │  (espejo one-way al DevOps del cliente)
        ▼
azure-pipelines-release.yml  (dispara por el tag)
        ├─ Infra_Validate (what-if)
        ├─ Infra_Deploy    → gate {env}-infra → az deployment group create (idempotente)
        └─ por cada app: Build_* → Deploy_* (espera Infra_Deploy) → gate {env}
```

- El tag despliega al **default `inte`** (test).
- **Promoción a UAT/PROD**: ejecutar el release manualmente eligiendo `environmentName`;
  cada ambiente corta en su approval gate.
- La infra es **idempotente**: actualiza en su lugar, no recrea el RG.

## Setup en el DevOps del cliente (una vez por ambiente)

1. **Service connection** ARM `sc-chacomer-eip-{env}` a la suscripción del cliente,
   con **Contributor + User Access Administrator** sobre el RG (las role assignments
   del Bicep necesitan poder asignar roles).
2. **Environments** `{env}` y `{env}-infra` con sus **approval gates**.
3. En `vars/env-config.yml`, poner el **RG real** de `uat`/`prod`.
4. Cargar los **secretos** de cada ambiente en su Key Vault (`az keyvault secret set`);
   ver `infra/README.md`. No viajan por el repo ni por el pipeline.

> Los pipelines vienen con `trigger: none` salvo el de release (`tag v*`), para que
> el espejo del repo no dispare corridas inesperadas. Axxon puede habilitar triggers
> de branch en su propio DevOps si quiere CI interno.
