# Contratos de mensajería — EiP

Este directorio guarda los contratos **legibles por máquina**:

| Archivo | Qué es |
|---|---|
| [eip-message-envelope.schema.json](eip-message-envelope.schema.json) | JSON Schema (draft 2020-12) del envelope estándar |
| [eip-message-envelope.example.json](eip-message-envelope.example.json) | Ejemplo de un mensaje válido |
| [dataverse-remote-execution-context.sample.json](dataverse-remote-execution-context.sample.json) | Muestra del formato nativo de Dataverse (interino) |

La explicación —qué significa cada campo, el naming de colas, el manejo de errores y hacia
dónde va el contrato— está en la wiki:
**[Arquitectura › Mensajería y contratos](../wiki/arquitectura/mensajeria.md)**.

> Si agregás o cambiás un contrato, actualizá esa página en el mismo PR.
