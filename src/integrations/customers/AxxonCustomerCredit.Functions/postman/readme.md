# Colección de Postman — Customer Credit

| Archivo | Qué es |
|---|---|
| `CustomerCredit.postman_collection.json` | Los 4 endpoints con sus filtros, más 3 casos que **esperan `400`** |
| `CustomerCredit-INTE.postman_environment.json` | Apunta a `fa-axxoncustomercredit-inte` |
| `CustomerCredit-Local.postman_environment.json` | Apunta a `http://localhost:7099` (`func start`) |

Importar los tres en Postman y elegir el environment antes de mandar nada.

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

El encadenado usa `pm.environment.set`, no variables de colección: en Postman el environment
tiene más precedencia, así que una variable de colección quedaría tapada por la del
environment y el request siguiente viajaría sin filtro.

## Correrla entera

```bash
npx newman run CustomerCredit.postman_collection.json -e CustomerCredit-INTE.postman_environment.json --env-var "functionKey=$KEY"
```

Verificado contra INTE el 2026-09-02: 15 requests, 26 assertions, todo en verde.

**`planes`, `cuotas` y `resoluciones` devuelven `cantidad: 0` y eso no es una falla**: esas
tres tablas están vacías en INTE. Por eso sus requests no afirman que haya filas — un test
que exija datos daría rojo por el estado del ambiente, no por el código.

## Los tres casos que esperan 400

No son relleno: son la garantía de que un filtro mal puesto **no** devuelve la tabla entera.
El caso importante es `cuenta` en `/creditos/resoluciones` — esa entidad no tiene
`CustomerAccount`, y si el endpoint lo ignorara, el consumidor recibiría un `200` con todo
creyendo que filtró por cliente.
