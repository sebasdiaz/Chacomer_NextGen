<!-- wiki-meta
sources: []
-->
<!-- GENERADO por docs/wiki/generate.mjs. No editar a mano: los cambios se pierden. -->
# Páginas generadas

Estas páginas **se generan desde el código** con [`generate.mjs`](../generate.mjs) y no se
editan a mano: cualquier cambio se pierde en la próxima corrida. Por eso no pueden quedar
desactualizadas.

```bash
node docs/wiki/generate.mjs
```

El CI corre `--check`: si alguien cambia el código y no regenera, el check falla.

| Página | Se genera desde |
|---|---|
| [Inventario de funciones](funciones.md) | `src/integrations/**/*.cs` + `infra/main.bicep` |
| [Colas del Service Bus](colas.md) | `infra/modules/servicebus.bicep` |
| [Application Settings por app](app-settings.md) | `infra/main.bicep` + `infra/modules/functionApp.bicep` |
| [Matriz de pipelines](pipelines.md) | `pipelines/*.yml` |
| [Controles PCF](controles-pcf.md) | `**/ControlManifest.Input.xml` |
