<!-- wiki-meta
sources:
  - pipelines/**
last_reviewed: 2026-08-24
-->

# Pipelines

Los pipelines viven en Azure DevOps (`CHACOMER/nexgen-ado-d365`) y corren desde el repo de
**ADO**, no desde el de GitHub. Hay uno por integración más dos de infraestructura.

## Pipelines de integración

Los seis extienden [`templates/functionapp-build-deploy.yml`](../../../pipelines/templates/functionapp-build-deploy.yml):
compilan **una sola vez** y promueven el mismo artifact en cadena.

```
Build ──► Deploy_inte (fa-axxon{dominio}-inte) ──► Deploy_test (fa-axxon{dominio}-test)
                                                   └── gate: approval del environment 'test'
```

Qué app despliega cada uno, a qué ambientes y con qué paths de disparo:
**[Matriz de pipelines](../_generado/pipelines.md)** (generado desde los `.yml`).

Lo que la matriz no explica:

| Caso | Por qué |
|---|---|
| `fiscal` no va a INTE | La app `fa-axxonfiscal-inte` no existe: la integración es nueva y en INTE el Bicep no crea Function Apps. Estrena directo en TEST |
| `thinkchat` no va a TEST | `fa-axxonthinkchat-test` todavía no existe. Se prende cuando el pipeline de infra TEST la cree, y después de asignarle los roles a mano |
| `customergroups` despliega a `fa-axxoncustomergroup` en INTE | Ahí la app se creó a mano con el nombre en singular. Se unifica en el cutover |

Todos disparan también ante cambios en su propio `.yml` y en los templates.

> **`src/core/**` dispara los seis.** Un cambio en `Axxon.Eip.Core` reconstruye y redespliega
> toda la plataforma; es a propósito, porque el core se arrastra por `ProjectReference` y
> queda embebido en cada artifact.

> **El pipeline de contacts no corre sus tests.** `tests/AxxonContacts.Functions.Tests`
> existe, pero el pipeline no le pasa `testProjectPath`, así que nunca se ejecuta.
> Es un hueco conocido, no una decisión.

### Autenticación

Sin secretos: el login sale de la **service connection federada** del ambiente
(`sc-chacomer-eip-{env}`) vía `AzureCLI@2` / OIDC. El SP de cada SC necesita `Contributor`
sobre el RG del ambiente y la SC tiene que estar autorizada para el pipeline.

### Overrides útiles del template

| Parámetro | Para qué |
|---|---|
| `deployToInte` / `deployToTest` | Dejar una integración fuera de un ambiente cuya app todavía no existe |
| `inteAppName` / `testAppName` | Cuando la app se creó a mano con otro nombre que el que genera `main.bicep` |
| `inteResourceGroup` / `testResourceGroup` | Default `DataverseINTE` / `dataversetest` |
| `testProjectPath` | Correr los tests antes del publish: si caen, no se genera artifact |
| `requiredPublishFiles` | Lista de archivos que TIENEN que quedar en el publish output. Para assets que no son código (templates, mappings): si un cambio de csproj deja de copiarlos, la app despliega "bien" y falla recién en la primera invocación |
| `smokeTestFunctionName` | Después del deploy, le pregunta al host si esa función quedó registrada. Un `config-zip` puede reportar éxito sin subir nada utilizable: el 404 aparecería recién cuando lo usa un usuario |

## Pipelines de infraestructura

| Pipeline | Ambiente | Disparo |
|---|---|---|
| [`azure-pipelines-infra.yml`](../../../pipelines/azure-pipelines-infra.yml) | INTE (`DataverseINTE`) | automático ante cambios en `infra/**` |
| [`azure-pipelines-infra-test.yml`](../../../pipelines/azure-pipelines-infra-test.yml) | TEST (`dataversetest`) | manual, con gate del environment `test-infra` |

## Validar el YAML sin correrlo

La API de **preview** de Azure DevOps compila un pipeline y devuelve el YAML ya expandido
sin encolar una corrida. Es la forma de detectar un error de template antes de pushear —
que es donde más aparecen, porque el error recién existe al expandir:

```bash
az devops invoke --organization https://dev.azure.com/CHACOMER --area pipelines --resource preview --http-method POST --route-parameters project=nexgen-ado-d365 pipelineId=<ID> --api-version 7.1-preview.1
```

> Los pipelines corren desde el repo de **ADO**. Validar el YAML del repo de GitHub no
> alcanza: hay que tener el cambio en la rama de ADO. Ver [Flujo de trabajo](../runbooks/doble-pr.md).

## Promoción del código a un ambiente nuevo

La infra crea las Function Apps vacías; el código lo pone el pipeline de cada
integración. Los 6 pipelines (`azure-pipelines-{contacts,customers,customergroups,products,fiscal,thinkchat}.yml`)
extienden `templates/functionapp-build-deploy.yml`, que compila **una sola vez** y
promueve el mismo artifact en cadena:

```
Build ──► Deploy_inte (fa-axxon{dominio}-inte) ──► Deploy_test (fa-axxon{dominio}-test)
                                                   └── gate: approval del environment 'test'
```

El binario que llega a TEST es exactamente el que se validó en INTE — no se
recompila. Para dejar una integración fuera de la promoción, pasarle
`deployToTest: false` en su pipeline.

Alta de un ambiente nuevo, en orden:

1. RG creado y con `Contributor` + `User Access Administrator` para la SP de la SC.
2. Service connection `sc-chacomer-eip-{env}` en Azure DevOps.
3. Environments `{env}` y `{env}-infra` en Azure DevOps, con approvals.
4. Correr el pipeline de infra → crea recursos compartidos + las 5 apps vacías.
5. Cargar los secrets del Key Vault (sección anterior).
6. Application User de cada MI en el Dataverse del ambiente + usuario S2S en F&O.
7. Correr los 5 pipelines de integración.

