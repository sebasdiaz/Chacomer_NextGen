<!-- wiki-meta
sources:
  - src/integrations/**/Functions/*SyncFunction.cs
last_reviewed: 2026-09-04
-->

# Runbook — el timer no corre

**Síntoma.** La Function App está en `Running`. No hay excepciones, no hay requests
fallidos, no hay alertas. Simplemente el sync nunca se ejecuta.

Afecta a las cuatro funciones con `TimerTrigger`: `CustomerGroupSyncFunction`,
`ProductGroupSyncFunction`, `ReleasedProductSyncFunction` y `ThinkchatTemplateSyncFunction`.

## Causa #1: el app setting del CRON está mal escrito

El binding pide una clave jerárquica (`%Schedules:CustomerGroupSync%`) y el host mapea
`__` a `:` al leer variables de entorno. **El setting va con doble guion bajo:**
`Schedules__CustomerGroupSync`. Escrito de cualquier otra forma
(`SchedulesCustomerGroupSync`, `Schedules.CustomerGroupSync`) el placeholder no resuelve,
el host no indexa la Function y la app arranca igual.

En los logs del **host**, al iniciar, aparecen sólo estos dos traces:

```
The 'CustomerGroupSyncFunction' function is in error:
  '%Schedules:CustomerGroupSync%' does not resolve to a value.
No job functions found.
```

### Chequeo rápido, sin esperar al horario

`GET https://<app>.azurewebsites.net/admin/functions/<NombreDeLaFuncion>/status` con la
master key: devuelve `{}` si la función indexó bien, o el error si no.

### Dónde está mal, hoy

Las apps de **INTE** están fuera del Bicep y tienen los settings escritos sin separador.
`fa-axxoncustomergroup` viene fallando así. Se corrige en el cutover — ver
[Ambientes](../plataforma/ambientes.md#cutover-de-inte). Las apps que administra el Bicep
lo reciben bien: `main.bicep` los emite como `Schedules__*`.

## Causa #2: el CRON corre en otra zona horaria

El CRON se evalúa en **UTC** salvo que la app tenga `WEBSITE_TIME_ZONE`. Para las 23:00 de
Asunción: `WEBSITE_TIME_ZONE = "Paraguay Standard Time"`.

Si el sync corre pero a una hora inesperada, es esto — no el punto anterior.

## Causa #3: la app perdió la identidad y no arranca del todo

Con `AzureWebJobsStorage` por identidad, una app sin sus role assignments no levanta.
Pasa en los ambientes que van con `deployRoleAssignments = false` cada vez que el
deployment recrea la app. Ver
[Ambientes › INTE: thinkchat con los roles a mano](../plataforma/ambientes.md#inte-thinkchat-con-los-roles-a-mano).
