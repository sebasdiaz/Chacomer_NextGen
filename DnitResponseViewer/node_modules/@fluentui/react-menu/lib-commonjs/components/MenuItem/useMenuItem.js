'use client';
"use strict";
Object.defineProperty(exports, "__esModule", {
    value: true
});
Object.defineProperty(exports, "useMenuItem_unstable", {
    enumerable: true,
    get: function() {
        return useMenuItem_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _reacticons = require("@fluentui/react-icons");
const _useMenuItemBase = require("./useMenuItemBase");
const ChevronRightIcon = (0, _reacticons.bundleIcon)(_reacticons.ChevronRightFilled, _reacticons.ChevronRightRegular);
const ChevronLeftIcon = (0, _reacticons.bundleIcon)(_reacticons.ChevronLeftFilled, _reacticons.ChevronLeftRegular);
const useMenuItem_unstable = (props, ref)=>{
    const { dir } = (0, _reactsharedcontexts.useFluent_unstable)();
    const state = (0, _useMenuItemBase.useMenuItemBase_unstable)(props, ref);
    // Set default chevron icon
    if (state.submenuIndicator) {
        var _state_submenuIndicator;
        var _children;
        (_children = (_state_submenuIndicator = state.submenuIndicator).children) !== null && _children !== void 0 ? _children : _state_submenuIndicator.children = dir === 'rtl' ? /*#__PURE__*/ _react.createElement(ChevronLeftIcon, null) : /*#__PURE__*/ _react.createElement(ChevronRightIcon, null);
    }
    return state;
};
