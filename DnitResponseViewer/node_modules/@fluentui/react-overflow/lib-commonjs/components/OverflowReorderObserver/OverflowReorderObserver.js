'use client';
"use strict";
Object.defineProperty(exports, "__esModule", {
    value: true
});
Object.defineProperty(exports, "OverflowReorderObserver", {
    enumerable: true,
    get: function() {
        return OverflowReorderObserver;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _overflowContext = require("../../overflowContext");
const OverflowReorderObserver = ()=>{
    const containerRef = (0, _overflowContext.useOverflowContext)((v)=>v.containerRef);
    const updateOverflow = (0, _overflowContext.useOverflowContext)((v)=>v.updateOverflow);
    const { targetDocument } = (0, _reactsharedcontexts.useFluent_unstable)();
    const targetWindow = targetDocument === null || targetDocument === void 0 ? void 0 : targetDocument.defaultView;
    _react.useEffect(()=>{
        const el = containerRef === null || containerRef === void 0 ? void 0 : containerRef.current;
        if (!el || !(targetWindow === null || targetWindow === void 0 ? void 0 : targetWindow.MutationObserver)) {
            return;
        }
        const mutationObserver = new targetWindow.MutationObserver(()=>updateOverflow());
        mutationObserver.observe(el, {
            childList: true
        });
        return ()=>mutationObserver.disconnect();
    }, [
        containerRef,
        updateOverflow,
        targetWindow
    ]);
    return null;
};
