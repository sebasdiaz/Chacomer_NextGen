/*
 * ATTENTION: The "eval" devtool has been used (maybe by default in mode: "development").
 * This devtool is neither made for production nor for readable output files.
 * It uses "eval()" calls to create a separate source file in the browser devtools.
 * If you are trying to read the output file, select a different devtool (https://webpack.js.org/configuration/devtool/)
 * or disable the default devtool with "devtool: false".
 * If you are looking for production-ready output files, see mode: "production" (https://webpack.js.org/configuration/mode/).
 */
var pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad;
/******/ (() => { // webpackBootstrap
/******/ 	"use strict";
/******/ 	var __webpack_modules__ = ({

/***/ "./DnitResponseViewer/DnitResponseViewer.tsx"
/*!***************************************************!*\
  !*** ./DnitResponseViewer/DnitResponseViewer.tsx ***!
  \***************************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   DnitResponseCard: () => (/* binding */ DnitResponseCard)\n/* harmony export */ });\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_0__);\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! @fluentui/react-components */ \"@fluentui/react-components\");\n/* harmony import */ var _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__);\n\n\n// ── Label map (Spanish display names) ─────────────────────────────────────\nvar LABEL_MAP = {\n  razonSocial: 'Razón Social',\n  ruc: 'RUC',\n  digitoVerificador: 'Dígito Verificador',\n  tipoContribuyente: 'Tipo Contribuyente',\n  tipoRegimen: 'Tipo Régimen',\n  estado: 'Estado',\n  calle: 'Calle',\n  numeroCasa: 'Número',\n  departamento: 'Departamento',\n  distrito: 'Distrito',\n  ciudad: 'Ciudad',\n  telefono: 'Teléfono',\n  email: 'Email',\n  fechaInscripcion: 'Fecha Inscripción',\n  fechaInicioActividades: 'Inicio Actividades'\n};\nvar FIELD_ORDER = ['razonSocial', 'ruc', 'digitoVerificador', 'tipoContribuyente', 'tipoRegimen', 'estado', 'calle', 'numeroCasa', 'departamento', 'distrito', 'ciudad', 'telefono', 'email', 'fechaInscripcion', 'fechaInicioActividades'];\nvar estadoColor = estado => {\n  switch ((estado !== null && estado !== void 0 ? estado : '').toUpperCase()) {\n    case 'ACTIVO':\n      return 'success';\n    case 'SUSPENDIDO':\n    case 'SUSPENSION TEMPORAL':\n    case 'NO VIGENTE':\n      return 'warning';\n    case 'CANCELADO':\n    case 'BLOQUEADO':\n      return 'danger';\n    default:\n      return 'informative';\n  }\n};\n// ── Styles ─────────────────────────────────────────────────────────────────\nvar useStyles = (0,_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.makeStyles)({\n  root: {\n    padding: '12px',\n    fontFamily: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontFamilyBase\n  },\n  header: {\n    display: 'flex',\n    alignItems: 'center',\n    gap: '10px',\n    marginBottom: '12px'\n  },\n  razonSocial: {\n    fontWeight: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontWeightSemibold,\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase400,\n    flex: '1'\n  },\n  grid: {\n    display: 'grid',\n    gridTemplateColumns: '1fr 1fr',\n    gap: '6px 24px',\n    marginTop: '10px'\n  },\n  field: {\n    display: 'flex',\n    flexDirection: 'column',\n    gap: '2px'\n  },\n  label: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase200,\n    textTransform: 'uppercase',\n    letterSpacing: '0.04em'\n  },\n  value: {\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase300,\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground1\n  },\n  divider: {\n    marginBottom: '8px'\n  },\n  rawJson: {\n    fontFamily: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontFamilyMonospace,\n    fontSize: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.fontSizeBase200,\n    whiteSpace: 'pre-wrap',\n    wordBreak: 'break-all',\n    backgroundColor: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralBackground3,\n    padding: '8px',\n    borderRadius: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.borderRadiusMedium,\n    maxHeight: '200px',\n    overflowY: 'auto'\n  },\n  empty: {\n    color: _fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.tokens.colorNeutralForeground3,\n    fontStyle: 'italic'\n  },\n  topFields: {\n    display: 'flex',\n    gap: '16px',\n    marginBottom: '8px',\n    flexWrap: 'wrap'\n  },\n  topField: {\n    display: 'flex',\n    flexDirection: 'column',\n    gap: '2px'\n  }\n});\n// ── Component ──────────────────────────────────────────────────────────────\nvar DnitResponseCard = _ref => {\n  var rawJson = _ref.rawJson;\n  var _a, _b, _c;\n  var styles = useStyles();\n  if (!rawJson || rawJson.trim() === '') {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      className: styles.root\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n      className: styles.empty\n    }, \"Sin respuesta de consulta RUC.\"));\n  }\n  var parsed = null;\n  var parseError = false;\n  try {\n    parsed = JSON.parse(rawJson);\n  } catch (_d) {\n    parseError = true;\n  }\n  if (parseError || !parsed) {\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      className: styles.root\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBar, {\n      intent: \"warning\"\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.MessageBarBody, null, \"No se pudo parsear la respuesta JSON.\")), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      className: styles.rawJson\n    }, rawJson));\n  }\n  var c = parsed.contribuyente;\n  var estado = (_a = c === null || c === void 0 ? void 0 : c.estado) !== null && _a !== void 0 ? _a : parsed === null || parsed === void 0 ? void 0 : parsed.estado;\n  // Build ordered field list, skip razonSocial and estado (shown in header)\n  var orderedKeys = [...FIELD_ORDER.filter(k => k !== 'razonSocial' && k !== 'estado'), ...Object.keys(c !== null && c !== void 0 ? c : {}).filter(k => !FIELD_ORDER.includes(k) && k !== 'razonSocial' && k !== 'estado')];\n  var fieldsToRender = orderedKeys.filter(k => c === null || c === void 0 ? void 0 : c[k]);\n  return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.root\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.header\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    className: styles.razonSocial\n  }, (_b = c === null || c === void 0 ? void 0 : c.razonSocial) !== null && _b !== void 0 ? _b : '—'), estado && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Badge, {\n    appearance: \"filled\",\n    color: estadoColor(estado),\n    size: \"medium\"\n  }, estado))), ((_c = parsed.codigo) !== null && _c !== void 0 ? _c : parsed.mensaje) && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(react__WEBPACK_IMPORTED_MODULE_0__.Fragment, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.topFields\n  }, parsed.codigo && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.topField\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    className: styles.label\n  }, \"C\\u00F3digo\"), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    className: styles.value\n  }, parsed.codigo))), parsed.mensaje && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.topField\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    className: styles.label\n  }, \"Mensaje\"), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n    className: styles.value\n  }, parsed.mensaje)))), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Divider, {\n    className: styles.divider\n  }))), c && fieldsToRender.length > 0 && (/*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.grid\n  }, fieldsToRender.map(key => {\n    var _a, _b;\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n      key: key,\n      className: styles.field\n    }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n      className: styles.label\n    }, (_a = LABEL_MAP[key]) !== null && _a !== void 0 ? _a : key), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Text, {\n      className: styles.value\n    }, (_b = c[key]) !== null && _b !== void 0 ? _b : '—'));\n  }))), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.Accordion, {\n    collapsible: true\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionItem, {\n    value: \"raw\"\n  }, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionHeader, {\n    size: \"small\"\n  }, \"JSON completo\"), /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(_fluentui_react_components__WEBPACK_IMPORTED_MODULE_1__.AccordionPanel, null, /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_0__.createElement(\"div\", {\n    className: styles.rawJson\n  }, JSON.stringify(parsed, null, 2))))));\n};\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./DnitResponseViewer/DnitResponseViewer.tsx?\n}");

/***/ },

/***/ "./DnitResponseViewer/index.ts"
/*!*************************************!*\
  !*** ./DnitResponseViewer/index.ts ***!
  \*************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   DnitResponseViewer: () => (/* binding */ DnitResponseViewer)\n/* harmony export */ });\n/* harmony import */ var _DnitResponseViewer__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(/*! ./DnitResponseViewer */ \"./DnitResponseViewer/DnitResponseViewer.tsx\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(/*! react */ \"react\");\n/* harmony import */ var react__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(react__WEBPACK_IMPORTED_MODULE_1__);\n\n\nclass DnitResponseViewer {\n  // eslint-disable-next-line @typescript-eslint/no-empty-function\n  constructor() {}\n  init(_context, _notifyOutputChanged, _state\n  // eslint-disable-next-line @typescript-eslint/no-empty-function\n  ) {}\n  updateView(context) {\n    var _a;\n    var raw = (_a = context.parameters.dnitResponse.raw) !== null && _a !== void 0 ? _a : \"\";\n    var props = {\n      rawJson: raw\n    };\n    return /*#__PURE__*/react__WEBPACK_IMPORTED_MODULE_1__.createElement(_DnitResponseViewer__WEBPACK_IMPORTED_MODULE_0__.DnitResponseCard, props);\n  }\n  getOutputs() {\n    return {};\n  }\n  // eslint-disable-next-line @typescript-eslint/no-empty-function\n  destroy() {}\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./DnitResponseViewer/index.ts?\n}");

/***/ },

/***/ "@fluentui/react-components"
/*!************************************!*\
  !*** external "FluentUIReactv940" ***!
  \************************************/
(module) {

module.exports = FluentUIReactv940;

/***/ },

/***/ "react"
/*!***************************!*\
  !*** external "Reactv16" ***!
  \***************************/
(module) {

module.exports = Reactv16;

/***/ }

/******/ 	});
/************************************************************************/
/******/ 	// The module cache
/******/ 	var __webpack_module_cache__ = {};
/******/ 	
/******/ 	// The require function
/******/ 	function __webpack_require__(moduleId) {
/******/ 		// Check if module is in cache
/******/ 		var cachedModule = __webpack_module_cache__[moduleId];
/******/ 		if (cachedModule !== undefined) {
/******/ 			return cachedModule.exports;
/******/ 		}
/******/ 		// Create a new module (and put it into the cache)
/******/ 		var module = __webpack_module_cache__[moduleId] = {
/******/ 			// no module.id needed
/******/ 			// no module.loaded needed
/******/ 			exports: {}
/******/ 		};
/******/ 	
/******/ 		// Execute the module function
/******/ 		if (!(moduleId in __webpack_modules__)) {
/******/ 			delete __webpack_module_cache__[moduleId];
/******/ 			var e = new Error("Cannot find module '" + moduleId + "'");
/******/ 			e.code = 'MODULE_NOT_FOUND';
/******/ 			throw e;
/******/ 		}
/******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
/******/ 	
/******/ 		// Return the exports of the module
/******/ 		return module.exports;
/******/ 	}
/******/ 	
/************************************************************************/
/******/ 	/* webpack/runtime/compat get default export */
/******/ 	(() => {
/******/ 		// getDefaultExport function for compatibility with non-harmony modules
/******/ 		__webpack_require__.n = (module) => {
/******/ 			var getter = module && module.__esModule ?
/******/ 				() => (module['default']) :
/******/ 				() => (module);
/******/ 			__webpack_require__.d(getter, { a: getter });
/******/ 			return getter;
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/define property getters */
/******/ 	(() => {
/******/ 		// define getter functions for harmony exports
/******/ 		__webpack_require__.d = (exports, definition) => {
/******/ 			for(var key in definition) {
/******/ 				if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
/******/ 					Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
/******/ 				}
/******/ 			}
/******/ 		};
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/hasOwnProperty shorthand */
/******/ 	(() => {
/******/ 		__webpack_require__.o = (obj, prop) => (Object.prototype.hasOwnProperty.call(obj, prop))
/******/ 	})();
/******/ 	
/******/ 	/* webpack/runtime/make namespace object */
/******/ 	(() => {
/******/ 		// define __esModule on exports
/******/ 		__webpack_require__.r = (exports) => {
/******/ 			if(typeof Symbol !== 'undefined' && Symbol.toStringTag) {
/******/ 				Object.defineProperty(exports, Symbol.toStringTag, { value: 'Module' });
/******/ 			}
/******/ 			Object.defineProperty(exports, '__esModule', { value: true });
/******/ 		};
/******/ 	})();
/******/ 	
/************************************************************************/
/******/ 	
/******/ 	// startup
/******/ 	// Load entry module and return exports
/******/ 	// This entry module can't be inlined because the eval devtool is used.
/******/ 	var __webpack_exports__ = __webpack_require__("./DnitResponseViewer/index.ts");
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('AxxonContacts.DnitResponseViewer', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.DnitResponseViewer);
} else {
	var AxxonContacts = AxxonContacts || {};
	AxxonContacts.DnitResponseViewer = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.DnitResponseViewer;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}