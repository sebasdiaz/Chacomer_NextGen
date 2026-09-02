# Colección de Postman — Customer Credit

| Archivo | Qué es |
|---|---|
| `CustomerCredit.postman_collection.json` | Los 4 endpoints con sus filtros, más 3 casos que **esperan `400`** |
| `CustomerCredit-INTE.postman_environment.json` | Apunta a `fa-axxoncustomercredit-inte` |
| `CustomerCredit-Local.postman_environment.json` | Apunta a `http://localhost:7099` (`func start`) |

Postman importa los `.json` (arrastrarlos a *Import → Files*). **No importa un `.zip`**:
ese formato lo acepta sólo para un data dump exportado por el propio Postman.

**La colección funciona sola, sin importar ningún environment**: trae las mismas variables
como defaults a nivel colección, apuntando a INTE. El environment sigue siendo lo cómodo
para saltar entre INTE y local, pero es opcional.

### Si el import del environment falla con un error de formato

Le faltan estos dos campos, que es lo que el importador usa para reconocer que el archivo
es un environment:

```json
"_postman_exported_at": "2026-09-02T18:00:00.000Z",
"_postman_exported_using": "Postman/11.19.0"
```

No alcanza con que el JSON sea válido y con que `_postman_variable_scope` esté puesto:
Newman carga el archivo igual sin ellos, así que el error aparece **sólo** al importar en la
app. Un environment escrito a mano los necesita.

## La function key

**Los environments vienen con `functionKey` vacío, a propósito.** Es un secreto y estos
archivos están versionados. Se completa a mano, en el environment, no en la colección:

```bash
az functionapp keys list -g DataverseINTE -n fa-axxoncustomercredit-inte --query "functionKeys.default" -o tsv
```

En el environment **Local** se puede dejar vacía: `func start` no valida la key.

La colección la manda como header `x-functions-key` vía la auth de nivel colección, así que
ningún request la lleva escrita.

## Variables

| Variable | Para qué |
|---|---|
| `baseUrl` | Hasta `/api`, sin barra final |
| `cuenta` | `CustomerAccount`. Default `302001`, que en INTE existe en 4 legal entities |
| `dataAreaId` | Legal entity, para acotar la lectura cross-company |
| `creditId`, `requestId` | Los llena solo **Planes › primeras N filas** cuando hay datos |
| `top` | 1 a 1000 |

El encadenado escribe **donde después se va a leer**: si hay un environment seleccionado va
ahí, y si no, a la variable de colección. No es un detalle cosmético — en Postman el
environment le gana en precedencia a la colección, así que escribir en la de colección con
un environment activo no serviría de nada y el request siguiente viajaría **sin filtro**,
devolviendo la tabla entera y pareciendo que funcionó.

## Correrla entera

```bash
npx newman run CustomerCredit.postman_collection.json -e CustomerCredit-INTE.postman_environment.json --env-var "functionKey=$KEY"
```

Verificado contra INTE el 2026-09-02, **con environment y sin él**: 15 requests, 26
assertions, todo en verde en los dos modos.

**`planes`, `cuotas` y `resoluciones` devuelven `cantidad: 0` y eso no es una falla**: esas
tres tablas están vacías en INTE. Por eso sus requests no afirman que haya filas — un test
que exija datos daría rojo por el estado del ambiente, no por el código.

## Los tres casos que esperan 400

No son relleno: son la garantía de que un filtro mal puesto **no** devuelve la tabla entera.
El caso importante es `cuenta` en `/creditos/resoluciones` — esa entidad no tiene
`CustomerAccount`, y si el endpoint lo ignorara, el consumidor recibiría un `200` con todo
creyendo que filtró por cliente.
