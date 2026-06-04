'use client';
"use strict";
Object.defineProperty(exports, "__esModule", {
    value: true
});
function _export(target, all) {
    for(var name in all)Object.defineProperty(target, name, {
        enumerable: true,
        get: all[name]
    });
}
_export(exports, {
    useTagGroupBase_unstable: function() {
        return useTagGroupBase_unstable;
    },
    useTagGroup_unstable: function() {
        return useTagGroup_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reacttabster = require("@fluentui/react-tabster");
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _useInteractionTagSecondaryStylesstyles = require("../InteractionTagSecondary/useInteractionTagSecondaryStyles.styles");
const useTagGroupBase_unstable = (props, ref)=>{
    const { onDismiss, disabled = false, defaultSelectedValues, dismissible = false, role = 'toolbar', onTagSelect, selectedValues, ...rest } = props;
    const [items, setItems] = (0, _reactutilities.useControllableState)({
        defaultState: defaultSelectedValues,
        state: selectedValues,
        initialState: []
    });
    const handleTagDismiss = (0, _reactutilities.useEventCallback)((e, data)=>{
        onDismiss === null || onDismiss === void 0 ? void 0 : onDismiss(e, data);
    });
    const handleTagSelect = (0, _reactutilities.useEventCallback)((0, _reactutilities.mergeCallbacks)(onTagSelect, (_, data)=>{
        if (items.includes(data.value)) {
            setItems(items.filter((item)=>item !== data.value));
        } else {
            setItems([
                ...items,
                data.value
            ]);
        }
    }));
    return {
        handleTagDismiss,
        handleTagSelect: onTagSelect ? handleTagSelect : undefined,
        selectedValues: items,
        role,
        disabled,
        dismissible,
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            role,
            'aria-disabled': disabled,
            ...rest
        }), {
            elementType: 'div'
        })
    };
};
const useTagGroup_unstable = (props, ref)=>{
    const { size = 'medium', appearance = 'filled' } = props;
    const { targetDocument } = (0, _reactsharedcontexts.useFluent_unstable)();
    const { findNextFocusable, findPrevFocusable } = (0, _reacttabster.useFocusFinders)();
    const arrowNavigationProps = (0, _reacttabster.useArrowNavigationGroup)({
        circular: true,
        axis: 'both',
        memorizeCurrent: true
    });
    const innerRef = _react.useRef(null);
    const mergedRef = (0, _reactutilities.useMergedRefs)(ref, innerRef);
    const enhancedOnDismiss = (0, _reactutilities.useEventCallback)((e, data)=>{
        var _props_onDismiss;
        (_props_onDismiss = props.onDismiss) === null || _props_onDismiss === void 0 ? void 0 : _props_onDismiss.call(props, e, data);
        const container = innerRef.current;
        const activeElement = targetDocument === null || targetDocument === void 0 ? void 0 : targetDocument.activeElement;
        if (container === null || container === void 0 ? void 0 : container.contains(activeElement)) {
            // focus on next tag only if the active element is within the current tag group
            const next = findNextFocusable(activeElement, {
                container
            });
            if (next) {
                next.focus();
                return;
            }
            // if there is no next focusable, focus on the previous focusable
            if (activeElement === null || activeElement === void 0 ? void 0 : activeElement.className.includes(_useInteractionTagSecondaryStylesstyles.interactionTagSecondaryClassNames.root)) {
                const prev = findPrevFocusable(activeElement.parentElement, {
                    container
                });
                prev === null || prev === void 0 ? void 0 : prev.focus();
            } else {
                const prev = findPrevFocusable(activeElement, {
                    container
                });
                prev === null || prev === void 0 ? void 0 : prev.focus();
            }
        }
    });
    return {
        ...useTagGroupBase_unstable({
            ...arrowNavigationProps,
            ...props,
            onDismiss: enhancedOnDismiss
        }, mergedRef),
        size,
        appearance
    };
};
