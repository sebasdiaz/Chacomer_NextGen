<!-- wiki-meta
sources: []
last_reviewed: 2026-08-21
-->

# Doble PR — GitHub y Azure DevOps

El proyecto vive en **dos repos** y cada cambio va en **dos pull requests**:

| Remoto | Repo | Para qué |
|---|---|---|
| `origin` | `github.com/sebasdiaz/Chacomer_NextGen` | Desarrollo y revisión del día a día |
| `cliente-nextgen` | `dev.azure.com/CHACOMER/nexgen-ado-d365/_git/Chacomer_NextGen` | Lo que ve el cliente y **desde donde corren los pipelines** |
| `cliente` | `dev.azure.com/CHACOMER/nexgen-ado-d365/_git/EiP` | Repo anterior de la EiP |

Los dos `main` **divergen**: no es un mirror. La rama de ADO se arma con `cherry-pick` de
los commits que ya entraron en GitHub.

## Consecuencias prácticas

- **Un cambio de pipeline no se prueba desde GitHub.** Los pipelines corren desde el repo de
  ADO: hasta que el commit no esté en una rama de ADO, el YAML nuevo no existe para
  Azure DevOps. Para validar antes de mergear, ver
  [Pipelines › Validar el YAML sin correrlo](../plataforma/pipelines.md#validar-el-yaml-sin-correrlo).
- **Los números de PR no coinciden** entre los dos repos. Al referenciar un PR, decir de
  cuál de los dos se habla.
- **Esta wiki viaja en el mismo commit que el código**, así que llega a ADO por el mismo
  camino. Es la razón principal por la que vive en `docs/wiki/` y no en una wiki de
  proyecto aparte.

## Pendiente de documentar

- El comando/secuencia exacta con la que se arma la rama de ADO (cherry-pick, rebase o
  merge) y quién la corre.
- Si el repo `EiP` sigue vivo o quedó congelado.
