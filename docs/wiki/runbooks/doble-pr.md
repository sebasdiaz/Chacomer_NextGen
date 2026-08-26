<!-- wiki-meta
sources: []
last_reviewed: 2026-08-25
-->

# Doble PR — GitHub y Azure DevOps

El proyecto vive en **dos repos** y cada cambio va en **dos pull requests**:

| Repo | Para qué |
|---|---|
| `github.com/sebasdiaz/Chacomer_NextGen` | Desarrollo y revisión del día a día |
| `dev.azure.com/CHACOMER/nexgen-ado-d365/_git/Chacomer_NextGen` | Lo que ve el cliente y **desde donde corren los pipelines** |
| `dev.azure.com/CHACOMER/nexgen-ado-d365/_git/EiP` | Repo anterior de la EiP |

> **El nombre del remoto es config de cada clon, no del proyecto.** Esta página listaba
> `cliente-nextgen` para el repo del cliente, pero hay clones donde ese remoto se llama
> `cliente` y el repo `EiP` no está configurado. Lo estable son las URLs: verificá con
> `git remote -v` antes de copiar un comando de acá.

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

## Armar la rama de ADO

Asumiendo que el remoto del cliente se llama `cliente` (verificar con `git remote -v`):

```bash
git fetch cliente
git checkout -b <rama> cliente/main
git cherry-pick <sha-de-github>          # uno por commit que va
```

Después **aplastar todo en un commit** antes de pushear:

```bash
git reset --soft cliente/main
git commit -F <mensaje>
```

El squash no es cosmético: `check-freshness.mjs` exige que la wiki se actualice **en el
mismo commit** que toca sus sources, así que un commit de código y otro de docs falla el
check aunque el contenido esté completo. Y en ADO el historial ya diverge, así que un
commit por cambio se lee mejor que la cadena de GitHub.

**Verificar sobre el árbol de ADO, no sobre el de GitHub.** Los `main` divergen en decenas
de commits: que algo compile en uno no prueba que compile en el otro. Correr build, tests,
`generate.mjs` y `check-freshness.mjs --diff cliente/main` con la rama de ADO activa.

Push y PR:

```bash
git push cliente HEAD:refs/heads/<rama>
```

```bash
az repos pr create --organization "https://dev.azure.com/CHACOMER" --project "nexgen-ado-d365" --repository "Chacomer_NextGen" --source-branch "<rama>" --target-branch "main" --title "<titulo>" --description "$(cat <archivo.md>)"
```

> **El MCP de Azure DevOps no sirve para esto.** Lista proyectos, pero `repo_repository
> list` devuelve `[]` tanto por nombre como por GUID de proyecto — parece alcance del token.
> `az repos pr create` sí funciona con la sesión de `az login`. La descripción del PR tiene
> un límite de **4000 caracteres**: si es más larga, apoyarse en que el ADR viaja en el
> repo y enlazarlo en lugar de repetirlo.

### Qué commits llevar

El criterio es "los que ya entraron en GitHub", pero conviene mirar el conjunto: si el
cambio se partió en dos PRs (código y wiki, por ejemplo) y solo uno está mergeado, llevar
únicamente ese le entrega al cliente una versión incompleta. En ese caso vale llevar los
dos y decirlo en el PR de ADO.

## Pendiente de documentar

- Si el repo `EiP` sigue vivo o quedó congelado.
