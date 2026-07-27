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

/***/ "./RucValidatorControl/index.ts"
/*!**************************************!*\
  !*** ./RucValidatorControl/index.ts ***!
  \**************************************/
(__unused_webpack_module, __webpack_exports__, __webpack_require__) {

eval("{__webpack_require__.r(__webpack_exports__);\n/* harmony export */ __webpack_require__.d(__webpack_exports__, {\n/* harmony export */   RucValidatorControl: () => (/* binding */ RucValidatorControl)\n/* harmony export */ });\nvar __awaiter = undefined && undefined.__awaiter || function (thisArg, _arguments, P, generator) {\n  function adopt(value) {\n    return value instanceof P ? value : new P(function (resolve) {\n      resolve(value);\n    });\n  }\n  return new (P || (P = Promise))(function (resolve, reject) {\n    function fulfilled(value) {\n      try {\n        step(generator.next(value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function rejected(value) {\n      try {\n        step(generator[\"throw\"](value));\n      } catch (e) {\n        reject(e);\n      }\n    }\n    function step(result) {\n      result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected);\n    }\n    step((generator = generator.apply(thisArg, _arguments || [])).next());\n  });\n};\n// ── Mapeo estado API → valor OptionSet axx_fiscalstate ───────────────────────\nvar ESTADO_MAP = {\n  \"Activo\": 1,\n  \"Suspendido\": 2,\n  \"Cancelado\": 3,\n  \"Bloqueado\": 4,\n  \"No Vigente\": 5\n};\n// ── Nombres de campos en Dataverse ───────────────────────────────────────────\nvar FIELD_GOVERNMENT_ID = \"governmentid\";\nvar FIELD_FISCAL_STATE = \"axx_fiscalstate\";\nvar FIELD_DESCRIPTION = \"description\";\nclass RucValidatorControl {\n  constructor() {\n    // Estado interno\n    this._currentValue = \"\";\n    this._loading = false;\n  }\n  // ── init ─────────────────────────────────────────────────────────────────\n  init(context, notifyOutputChanged, _state, container) {\n    var _a;\n    this._context = context;\n    this._notifyOutputChanged = notifyOutputChanged;\n    this._container = container;\n    this._currentValue = (_a = context.parameters.DocumentNumber.raw) !== null && _a !== void 0 ? _a : \"\";\n    this._buildUI();\n  }\n  // ── updateView ───────────────────────────────────────────────────────────\n  updateView(context) {\n    var _a;\n    this._context = context;\n    var incoming = (_a = context.parameters.DocumentNumber.raw) !== null && _a !== void 0 ? _a : \"\";\n    if (incoming !== this._input.value && !this._loading) {\n      this._input.value = incoming;\n      this._currentValue = incoming;\n      this._clearStatus();\n    }\n    this._input.disabled = context.mode.isControlDisabled || this._loading;\n    this._button.disabled = context.mode.isControlDisabled || this._loading;\n  }\n  // ── getOutputs ───────────────────────────────────────────────────────────\n  getOutputs() {\n    return {\n      DocumentNumber: this._currentValue\n    };\n  }\n  // ── destroy ──────────────────────────────────────────────────────────────\n  destroy() {}\n  // ── UI builder ───────────────────────────────────────────────────────────\n  _buildUI() {\n    // Wrapper\n    var wrapper = document.createElement(\"div\");\n    wrapper.className = \"ruc-wrapper\";\n    // Input row\n    var inputRow = document.createElement(\"div\");\n    inputRow.className = \"ruc-input-row\";\n    this._input = document.createElement(\"input\");\n    this._input.type = \"text\";\n    this._input.className = \"ruc-input\";\n    this._input.value = this._currentValue;\n    this._input.placeholder = \"Ej: 80012345-0\";\n    this._input.addEventListener(\"input\", () => this._onInputChange());\n    this._input.addEventListener(\"keydown\", e => {\n      if (e.key === \"Enter\") this._validate();\n    });\n    this._button = document.createElement(\"button\");\n    this._button.type = \"button\";\n    this._button.className = \"ruc-btn\";\n    this._button.innerHTML = \"&#128269; Validar\";\n    this._button.addEventListener(\"click\", () => this._validate());\n    inputRow.appendChild(this._input);\n    inputRow.appendChild(this._button);\n    // Status row (oculto por defecto)\n    this._statusRow = document.createElement(\"div\");\n    this._statusRow.className = \"ruc-status hidden\";\n    this._statusIcon = document.createElement(\"span\");\n    this._statusIcon.className = \"ruc-status-icon\";\n    this._statusText = document.createElement(\"span\");\n    this._statusText.className = \"ruc-status-text\";\n    this._statusRow.appendChild(this._statusIcon);\n    this._statusRow.appendChild(this._statusText);\n    wrapper.appendChild(inputRow);\n    wrapper.appendChild(this._statusRow);\n    this._container.appendChild(wrapper);\n  }\n  // ── event handlers ───────────────────────────────────────────────────────\n  _onInputChange() {\n    this._currentValue = this._input.value;\n    this._clearStatus();\n    this._notifyOutputChanged();\n  }\n  // ── validacion ───────────────────────────────────────────────────────────\n  _validate() {\n    return __awaiter(this, void 0, void 0, function* () {\n      var _a, _b;\n      var ruc = (_a = this._input.value) === null || _a === void 0 ? void 0 : _a.trim();\n      if (!ruc) {\n        this._showStatus(\"error\", \"Ingrese un RUC antes de validar.\");\n        return;\n      }\n      this._setLoading(true);\n      try {\n        var contribuyente = yield this._callApi(ruc);\n        if (!contribuyente) {\n          this._showStatus(\"error\", \"RUC no encontrado en la SET.\");\n          return;\n        }\n        // Actualizar campos del formulario via Xrm\n        this._updateFormFields(contribuyente);\n        this._showStatus(\"success\", \"\".concat(contribuyente.razonSocial, \" \\u2014 \").concat(contribuyente.estado));\n        // Persistir el valor formateado (ej: \"80012345-0\")\n        this._currentValue = (_b = contribuyente.ruc) !== null && _b !== void 0 ? _b : ruc;\n        this._input.value = this._currentValue;\n        this._notifyOutputChanged();\n      } catch (err) {\n        var msg = err instanceof Error ? err.message : \"Error al conectar con la API.\";\n        this._showStatus(\"error\", msg);\n      } finally {\n        this._setLoading(false);\n      }\n    });\n  }\n  // ── llamada a la Azure Function ──────────────────────────────────────────\n  _callApi(ruc) {\n    return __awaiter(this, void 0, void 0, function* () {\n      var _a, _b, _c;\n      var base = ((_a = this._context.parameters.ApiBaseUrl.raw) !== null && _a !== void 0 ? _a : \"\").replace(/\\/$/, \"\");\n      var apiKey = (_b = this._context.parameters.ApiKey.raw) !== null && _b !== void 0 ? _b : \"\";\n      var qs = apiKey ? \"?code=\".concat(encodeURIComponent(apiKey)) : \"\";\n      var url = \"\".concat(base, \"/api/turuc/contribuyente/\").concat(encodeURIComponent(ruc)).concat(qs);\n      var response = yield fetch(url, {\n        method: \"GET\",\n        headers: {\n          \"Accept\": \"application/json\"\n        }\n      });\n      if (response.status === 404) return null;\n      if (!response.ok) {\n        throw new Error(\"HTTP \".concat(response.status, \" al consultar la API de TURUC.\"));\n      }\n      var body = yield response.json();\n      if (!body.data || ((_c = body.message) === null || _c === void 0 ? void 0 : _c.toUpperCase()) !== \"OK\") return null;\n      return body.data;\n    });\n  }\n  // ── actualizar campos Dataverse via Xrm ─────────────────────────────────\n  _updateFormFields(c) {\n    // eslint-disable-next-line @typescript-eslint/no-explicit-any\n    var xrm = window.Xrm;\n    if (!xrm) return;\n    var formContext = xrm.Page;\n    if (!formContext) return;\n    // governmentid = ruc formateado (ej: \"80012345-0\")\n    this._setTextField(formContext, FIELD_GOVERNMENT_ID, c.ruc);\n    // description = respuesta completa JSON\n    this._setTextField(formContext, FIELD_DESCRIPTION, JSON.stringify(c, null, 2));\n    // axx_fiscalstate = MultiSelectPicklist — array de { value }\n    var estadoVal = ESTADO_MAP[c.estado];\n    if (estadoVal !== undefined) {\n      var attr = formContext.getAttribute(FIELD_FISCAL_STATE);\n      if (attr) {\n        attr.setValue([estadoVal]);\n        attr.fireOnChange();\n      }\n    }\n  }\n  _setTextField(formContext, fieldName, value) {\n    if (!value) return;\n    var attr = formContext.getAttribute(fieldName);\n    if (attr) {\n      attr.setValue(value);\n      attr.fireOnChange();\n    }\n  }\n  // ── helpers UI ───────────────────────────────────────────────────────────\n  _setLoading(loading) {\n    this._loading = loading;\n    this._button.disabled = loading;\n    this._input.disabled = loading;\n    this._button.innerHTML = loading ? \"&#9203; Validando...\" : \"&#128269; Validar\";\n  }\n  _showStatus(type, message) {\n    this._statusRow.className = \"ruc-status ruc-status--\".concat(type);\n    this._statusIcon.textContent = type === \"success\" ? \"✅\" : type === \"warning\" ? \"⚠️\" : \"❌\";\n    this._statusText.textContent = message;\n  }\n  _clearStatus() {\n    this._statusRow.className = \"ruc-status hidden\";\n    this._statusText.textContent = \"\";\n  }\n}\n\n//# sourceURL=webpack://pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad/./RucValidatorControl/index.ts?\n}");

/***/ }

/******/ 	});
/************************************************************************/
/******/ 	// The require scope
/******/ 	var __webpack_require__ = {};
/******/ 	
/************************************************************************/
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
/******/ 	var __webpack_exports__ = {};
/******/ 	__webpack_modules__["./RucValidatorControl/index.ts"](0,__webpack_exports__,__webpack_require__);
/******/ 	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = __webpack_exports__;
/******/ 	
/******/ })()
;
if (window.ComponentFramework && window.ComponentFramework.registerControl) {
	ComponentFramework.registerControl('AxxonContacts.RucValidatorControl', pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.RucValidatorControl);
} else {
	var AxxonContacts = AxxonContacts || {};
	AxxonContacts.RucValidatorControl = pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad.RucValidatorControl;
	pcf_tools_652ac3f36e1e4bca82eb3c1dc44e6fad = undefined;
}