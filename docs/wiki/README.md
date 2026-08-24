<!-- wiki-meta
sources: []
last_reviewed: 2026-08-21
-->

# Wiki de la EiP — Chacomer NextGen

Documentación de la **Enterprise Integration Platform**: qué hace cada integración, cómo
está armada la plataforma y qué hacer cuando algo falla.

Vive en `docs/wiki/` del repo, así que se revisa en el mismo PR que el código y se publica
a Azure DevOps con *Publish code as wiki*. **No hay otra fuente de verdad**: si algo está
documentado en dos lugares, uno de los dos está desactualizado.

## Índice

| Sección | Qué hay |
|---|---|
| [Arquitectura](arquitectura.md) | Visión general, contratos de mensajería, decisiones de diseño |
| [Plataforma](plataforma.md) | Core, secretos, telemetría, infraestructura, ambientes, pipelines |
| [Integraciones](integraciones.md) | Una página por dominio: contacts, customers, customer groups, products, fiscal, thinkchat |
| [Web resources](webresources.md) | Los controles PCF |
| [Runbooks](runbooks.md) | Qué hacer cuando pasa algo |
| [Generado](_generado/README.md) | Inventarios que salen del código: funciones, colas, app settings, pipelines, controles PCF |

### Atajos

- ¿Qué es todo esto? → [Visión general](arquitectura/vision-general.md)
- ¿Por qué está hecho así? → [Decisiones](arquitectura/decisiones.md)
- ¿Dónde corre y cómo se despliega? → [Ambientes](plataforma/ambientes.md) · [Pipelines](plataforma/pipelines.md)
- ¿Dónde miro los logs? → [Telemetría](plataforma/telemetria.md)

## Cómo se mantiene

La wiki se actualiza **en el mismo commit que el cambio que la vuelve falsa**. No hay una
pasada de documentación al final del sprint: eso es exactamente lo que la deja atrás.

### 1. Cada página declara de qué código depende

Arriba de todo, en un comentario HTML invisible al renderizar:

```markdown
<!-- wiki-meta
sources:
  - src/integrations/contacts/**
  - pipelines/azure-pipelines-contacts.yml
last_reviewed: 2026-08-21
-->
```

Ese bloque es el contrato: **si tu cambio toca alguno de esos globs, la página se revisa en
el mismo PR**, y `last_reviewed` se actualiza. Es también lo que permite automatizar el
resto. Está declarado en [`CLAUDE.md`](../../CLAUDE.md), así que aplica por default cuando
trabajás con Claude Code.

Hay dos claves posibles:

| Clave | Dispara cuando | Para qué páginas |
|---|---|---|
| `sources:` | cambia **cualquier** archivo que matchee | Las que documentan comportamiento |
| `sources_new:` | se **agrega o borra** un archivo que matchee | Las que son un inventario y no se vuelven falsas porque alguien edite el cuerpo de una función |

Sin ninguna de las dos (o con `[]`), la página no se chequea por frescura — es el caso de
los índices de navegación.

### 2. El check lo verifica solo

[`check-freshness.mjs`](check-freshness.mjs) contesta tres preguntas: qué páginas no tienen
`wiki-meta`, cuáles dependen de código que cambió después de su `last_reviewed`, y qué links
relativos están rotos. No tiene dependencias: se corre con node, sin `npm install`.

```bash
node docs/wiki/check-freshness.mjs
```

Lo que este branch vuelve revisable, que es lo que mira el CI en cada PR:

```bash
node docs/wiki/check-freshness.mjs --diff main
```

En [GitHub Actions](../../.github/workflows/wiki-freshness.yml) el aviso de frescura **no
traba el merge** — decidir si la página hay que tocarla es humano. Lo que sí falla el check
es un link roto o una página sin `wiki-meta`. Los lunes corre el reporte completo.

Para actualizar las páginas afectadas hay un slash command en Claude Code:

```
/wiki-sync
```

Lee el diff, decide página por página qué cambió de verdad, y actualiza `last_reviewed`
también en las que revisó y no hizo falta tocar.

### 3. Qué se escribe y qué se genera

| Se **escribe** a mano | Se **genera** desde el código |
|---|---|
| Por qué una cola lleva sessions y otra no | El inventario de funciones y sus triggers |
| Por qué un 400 de F&O va al DLQ | Las colas del namespace y su configuración |
| Runbooks y decisiones | Los app settings de cada app, leídos del Bicep |
| Los mapeos y sus casos borde | La matriz pipeline → app → ambiente |

Lo generado no admite edición manual: se regenera y por eso no puede mentir. Lo escrito es
corto, y es lo único que exige disciplina.

Las páginas de [`_generado/`](_generado/README.md) las produce
[`generate.mjs`](generate.mjs) leyendo el código:

```bash
node docs/wiki/generate.mjs
```

El CI corre `node docs/wiki/generate.mjs --check`, que **falla** si alguien cambió el código
y no regeneró. A diferencia del aviso de frescura, esto sí traba: no es una opinión sobre si
la página hace falta actualizar, es un hecho verificable.

### 4. Marcar lo que falta, no inventarlo

Varias páginas terminan en **Pendiente de documentar**. Esa sección vale más que un párrafo
plausible: dice explícitamente qué no sabemos todavía. Al completarlo, se borra la línea.

### Pendiente de armar

- [ ] Publicar la carpeta como wiki en Azure DevOps (*Publish code as wiki*), apuntando a
      `docs/wiki` de la rama `main` del repo de ADO.
- [ ] Sumar al generador los mapeos CRM → F&O de
      [Customers](integraciones/customers.md#mapeo-por-json-no-hardcodeado), que hoy se
      describen a mano y salen de los JSON de `Mappings/`.
