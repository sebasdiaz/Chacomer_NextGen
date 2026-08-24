# Chacomer NextGen — EiP

Integraciones entre Dynamics 365 (Dataverse), Finance & Operations y satélites externos:
Azure Functions .NET 10 sobre Azure Service Bus, infra en Bicep, un pipeline por
integración. La documentación está en [`docs/wiki/`](docs/wiki/README.md).

## Regla de oro: la wiki se actualiza en el mismo commit

Cada página de `docs/wiki/` declara de qué código depende, en un bloque `wiki-meta`:

```markdown
<!-- wiki-meta
sources:
  - src/integrations/contacts/**
  - pipelines/azure-pipelines-contacts.yml
last_reviewed: 2026-08-21
-->
```

**Antes de dar por terminado un cambio de código, buscá qué páginas lo declaran como
`source` y actualizalas en el mismo commit**, subiendo `last_reviewed` a la fecha de hoy.
No es una pasada de documentación al final: es parte del cambio.

Para saber qué páginas toca lo que estás por commitear:

```bash
node docs/wiki/check-freshness.mjs --diff main
```

Parte de la wiki **se genera desde el código** (`docs/wiki/_generado/`: inventario de
funciones, colas, app settings, matriz de pipelines, controles PCF). Esas páginas no se
editan a mano — se regeneran, y el CI falla si quedaron atrás:

```bash
node docs/wiki/generate.mjs
```

Si agregás una Function, una cola, un app setting, un pipeline o un control PCF, corré el
generador antes de terminar.

Guías al escribir en la wiki:

- **Una sola fuente por tema.** Si el dato ya está en otra página, enlazala en lugar de
  repetirla. La duplicación es lo que se desactualiza.
- **Documentá el porqué, no el qué.** Lo que se lee del código (nombres de funciones,
  settings, colas) es candidato a generarse; lo valioso es la razón de cada decisión y
  cómo se rompe cada cosa en la práctica.
- **Si no sabés algo, va en "Pendiente de documentar".** Una línea honesta vale más que un
  párrafo plausible.
- Las decisiones de diseño se anotan en
  [`docs/wiki/arquitectura/decisiones.md`](docs/wiki/arquitectura/decisiones.md).

## Build y tests

```bash
dotnet build Chacomer.sln -c Release
```

```bash
dotnet test tests/AxxonCustomers.Functions.Tests/AxxonCustomers.Functions.Tests.csproj
```

Los tests de customers corren en el pipeline antes del publish. Los de contacts existen
pero el pipeline todavía no los ejecuta.

## Convenciones

- **Commits en español**, en minúscula, con el área adelante:
  `customers: sellar msdyn_sellable al calificar el prospecto (#67)`.
  Áreas en uso: `contacts`, `customers`, `products`, `fiscal`, `thinkchat`, `infra`,
  `pipelines`, `docs`.
- **Doble PR**: cada cambio va a GitHub (`origin`) y a Azure DevOps (`cliente-nextgen`).
  Los pipelines corren desde el repo de ADO. Ver
  [`docs/wiki/runbooks/doble-pr.md`](docs/wiki/runbooks/doble-pr.md).
- **No commitear ni pushear sin que lo pidan.** Dejar los cambios en el working tree.
- El código nuevo se parece al que lo rodea: mismos nombres, misma densidad de comentarios,
  mismos patrones. El core (`Axxon.Eip.Core`) es la primera parada antes de escribir algo
  cross.

## Dónde mirar antes de tocar algo

| Tema | Página |
|---|---|
| Cómo arranca cada Function App | [Axxon.Eip.Core](docs/wiki/plataforma/eip-core.md) |
| Secretos, Key Vault, Managed Identity | [Secretos y Key Vault](docs/wiki/plataforma/secretos-y-key-vault.md) |
| Colas, RBAC, qué crea el Bicep | [Infraestructura](docs/wiki/plataforma/infraestructura.md) |
| Estado real de INTE y TEST | [Ambientes](docs/wiki/plataforma/ambientes.md) |
| Contrato de los mensajes | [Mensajería](docs/wiki/arquitectura/mensajeria.md) |
