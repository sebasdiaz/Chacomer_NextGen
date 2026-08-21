---
description: Actualiza las páginas de docs/wiki que el cambio actual vuelve falsas
argument-hint: "[base] (default: main)"
allowed-tools: Bash(node docs/wiki/check-freshness.mjs:*), Bash(node docs/wiki/generate.mjs:*), Bash(git diff:*), Bash(git log:*), Bash(git status:*), Read, Edit, Grep, Glob
---

Base de comparación: **${1:-main}**

Páginas que el cambio vuelve revisables:

!`node docs/wiki/check-freshness.mjs --diff ${1:-main}`

Archivos que cambiaron:

!`git diff --stat ${1:-main}...HEAD`

Cambios sin commitear:

!`git status --short`

---

## Tu tarea

Actualizá las páginas de `docs/wiki/` que el cambio de este branch vuelve falsas.

1. **Leé el diff real** (`git diff ${1:-main}...HEAD`, más los cambios sin commitear) y
   entendé qué cambió de verdad. El listado de arriba dice qué páginas *declaran* esos
   archivos como `source`; no dice que todas necesiten cambios.

2. **Para cada página listada**, abrila y decidí:
   - Si el cambio contradice algo escrito → corregilo.
   - Si el cambio agrega comportamiento que la página debería cubrir → agregalo, en el
     estilo de la página (tablas cortas, el porqué antes que el qué).
   - Si el cambio no afecta lo que la página dice → **no la toques**, y decilo en el
     resumen. Subir `last_reviewed` sin cambios es válido y significa "lo miré, sigue bien".

3. **Actualizá `last_reviewed`** a la fecha de hoy en toda página que hayas revisado.

4. **Si el cambio introduce una decisión de diseño** (se descartó una alternativa, se eligió
   un trade-off que alguien va a cuestionar), agregá la fila en
   `docs/wiki/arquitectura/decisiones.md` apuntando a dónde quedó explicada. Si no tiene
   página natural, creá un ADR desde `docs/wiki/arquitectura/decisiones/_template.md`.

5. **Si aparece una integración, cola, control PCF o pipeline nuevo**, agregalo también a
   los índices: `docs/wiki/integraciones.md`, `docs/wiki/webresources.md`,
   `docs/wiki/plataforma/pipelines.md` y el `.order` de la carpeta.

6. **Regenerá lo que sale del código**: `node docs/wiki/generate.mjs`. Las páginas de
   `docs/wiki/_generado/` **no se editan a mano** — si alguna quedó mal, el que está mal es
   el código o el generador.

7. **Cerrá corriendo `node docs/wiki/check-freshness.mjs`** y arreglá lo que reporte.

## Reglas

- **No inventes.** Si el diff no alcanza para saber cómo se comporta algo, leé el código.
  Si sigue sin estar claro, escribilo en la sección "Pendiente de documentar" de la página
  en vez de suponer.
- **Una sola fuente por tema.** Enlazá en lugar de duplicar.
- **No commitees ni pushees.** Dejá los cambios en el working tree.
- Al terminar, resumí en pocas líneas: qué páginas tocaste y por qué, cuáles revisaste sin
  cambiar, y qué quedó marcado como pendiente.
