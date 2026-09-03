# Colección de Postman — Fiscal (consultas por RUC)

| Archivo | Qué es |
|---|---|
| `Fiscal.postman_collection.json` | Los 11 endpoints agrupados por origen (SET, TURUC, Dataverse), más 4 casos que **esperan `400`** |
| `Fiscal-INTE.postman_environment.json` | Apunta a `fa-axxonfiscal-inte` |
| `Fiscal-TEST.postman_environment.json` | Apunta a `fa-axxonfiscal-test` |
| `Fiscal-Local.postman_environment.json` | Apunta a `http://localhost:7071` (`func start`) |

Postman importa los `.json` (arrastrarlos a *Import → Files*). **No importa un `.zip`**:
ese formato lo acepta sólo para un data dump exportado por el propio Postman.

**La colección funciona sola, sin importar ningún environment**: trae las mismas variables
como defaults a nivel colección, apuntando a INTE. El environment es lo cómodo para saltar
entre ambientes, pero es opcional.

Si el import de un environment falla con un error de formato, le faltan
`_postman_exported_at` y `_postman_exported_using` — está explicado en el
[readme de Customer Credit](../../../customers/AxxonCustomerCredit.Functions/postman/readme.md#si-el-import-del-environment-falla-con-un-error-de-formato).

## La function key

**Los environments vienen con `functionKey` vacío, a propósito.** Es un secreto y estos
archivos están versionados. Se completa a mano, en el environment, no en la colección:

```bash
az functionapp keys list -g DataverseINTE -n fa-axxonfiscal-inte --query "functionKeys.default" -o tsv
```

Para TEST, el resource group es `dataversetest` y la app `fa-axxonfiscal-test`. En **Local**
se puede dejar vacía: `func start` no valida la key.

La colección la manda como header `x-functions-key` vía la auth de nivel colección, así que
ningún request la lleva escrita. Los dos preflight `OPTIONS` van con `noauth` explícito:
son anónimos y la gracia es comprobar que siguen funcionando **sin** key.

## Los tres orígenes no se prueban igual

Es la decisión de diseño de la colección, y explica por qué los tests son desparejos:

| Grupo | Origen | Qué afirman los tests |
|---|---|---|
| **SET / DNIT** | API de la DNIT | Que la Function llegó al origen (no `502`/`504`) y devolvió JSON. **No** la forma del payload |
| **TURUC** | API pública de turuc.com.py | Igual que SET |
| **Dataverse** | Dataverse del ambiente | El sobre completo: `{ruc, cantidad, resultados}`, el `tipoPersona` derivado de la tabla y el match por prefijo |

SET y TURUC son **proxies**: la Function reenvía el JSON crudo sin re-serializar. Afirmar
campos de esas respuestas sería atarse al contrato de un tercero que puede cambiarlo sin
avisar, y un rojo ahí no diría nada sobre nuestro código. Dataverse sí tiene forma propia
—la definimos nosotros en `PartyLookupResult`— y por eso ahí los tests son específicos.

## Variables

| Variable | Para qué |
|---|---|
| `baseUrl` | Hasta `/api`, sin barra final |
| `ruc` | RUC **entero, con DV** (`80054203-7`). Lo usan TURUC y Dataverse |
| `rucSinDv` | El mismo RUC sin DV, para probar el match por prefijo de Dataverse |
| `setRuc` / `setDv` | La SET los pide **por separado**: `80054203` y `7` |
| `numeroTimbrado`, `tipoDocumento`, `numeroDocumento`, `fechaExpedicion`, `medioGeneracion` | Los cinco parámetros extra de validez de documento |
| `search`, `page`, `draw`, `start`, `length` | Búsqueda y paginado de TURUC |

**El RUC va partido sólo para la SET.** TURUC y Dataverse lo toman entero, y por eso hay
dos juegos de variables en vez de uno: unificarlos obligaría a partir el string en un
pre-request script para tres de los cuatro grupos.

**Los datos de timbrado son de ejemplo, no un comprobante real.** La SET va a contestar
`INVALIDO` y eso es una respuesta correcta del endpoint, no una falla. Para ver un `VALIDO`
hay que poner un timbrado real en las variables.

## Correrla entera

```bash
npx newman run Fiscal.postman_collection.json -e Fiscal-INTE.postman_environment.json --env-var "functionKey=$KEY"
```

**Todavía no está verificada contra ningún ambiente.** La colección se escribió leyendo el
código de las Functions, no grabando tráfico real: las rutas, los parámetros y los mensajes
de error que afirman los tests salen de `AxxonFiscal.Functions/Functions/*.cs`.

Antes de confiar en un rojo, mirar contra qué se está corriendo:

- **`fa-axxonfiscal-inte` puede no estar arriba.** La app es greenfield y nace sin sus role
  assignments (INTE va con `deployRoleAssignments = false`): sin *Storage Blob Data Owner*,
  *Storage Queue Data Contributor* y *Key Vault Secrets User* sobre su MI, **no arranca**.
- **Los tres endpoints de `/dataverse/*` devuelven `502`** hasta que la MI de la app esté
  dada de alta como Application User en el Dataverse del ambiente. SET y TURUC andan igual,
  porque no tocan Dataverse.

Los dos pasos están en
[Fiscal › Estado del despliegue](../../../../../docs/wiki/integraciones/fiscal.md#estado-del-despliegue).

## Los cuatro casos que esperan 400

No son relleno: son la garantía de que la Function **corta antes de salir a la red**. Sin
ellos, un parámetro faltante llegaría al origen y volvería como error de la SET o de TURUC,
que es mucho más caro de diagnosticar que un `400` que nombra el campo.

El caso que más importa es `dataverse/consulta-ruc` sin `ruc`: si eso devolviera `200`,
sería volcar contacts y accounts del ambiente por un parámetro que nadie mandó.

Y `validez-documento-timbrado` incompleto devuelve **los seis faltantes en un solo error**,
no de a uno por reintento — el test los verifica todos justamente para que esa propiedad no
se pierda en una refactorización.
