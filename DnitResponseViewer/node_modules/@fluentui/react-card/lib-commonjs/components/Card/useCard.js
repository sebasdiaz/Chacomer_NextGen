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
    useCardBase_unstable: function() {
        return useCardBase_unstable;
    },
    useCard_unstable: function() {
        return useCard_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reacttabster = require("@fluentui/react-tabster");
const _useCardSelectable = require("./useCardSelectable");
const _CardContext = require("./CardContext");
const focusMap = {
    off: undefined,
    'no-tab': 'limited-trap-focus',
    'tab-exit': 'limited',
    'tab-only': 'unlimited'
};
const interactiveEventProps = [
    'onClick',
    'onDoubleClick',
    'onMouseUp',
    'onMouseDown',
    'onPointerUp',
    'onPointerDown',
    'onTouchStart',
    'onTouchEnd',
    'onDragStart',
    'onDragEnd'
];
/**
 * Compute whether a Card is interactive based on the presence of pointer/mouse
 * event props and the disabled flag. This intentionally does not depend on
 * focus management utilities so it can be used from headless contexts.
 */ const computeInteractive = (props)=>{
    if (props.disabled) {
        return false;
    }
    return interactiveEventProps.some((prop)=>props[prop] !== undefined);
};
const useCard_unstable = (props, ref)=>{
    const { appearance = 'filled', orientation = 'vertical', size = 'medium', ...cardProps } = props;
    const { disabled = false, focusMode: focusModeProp } = props;
    // Focus-within ref drives the styled focus outline; merged with the user ref
    // before being passed down so the base hook does not depend on react-tabster.
    const focusWithinRef = (0, _reacttabster.useFocusWithin)();
    const cardRef = (0, _reactutilities.useMergedRefs)(focusWithinRef, ref);
    // Focus-aware predicate that prevents toggling the selection when the user
    // interacts with an inner focusable element.
    const { findAllFocusable } = (0, _reacttabster.useFocusFinders)();
    const shouldRestrictTriggerAction = _react.useCallback((event)=>{
        if (!focusWithinRef.current) {
            return false;
        }
        const focusableElements = findAllFocusable(focusWithinRef.current);
        const target = event.target;
        return focusableElements.some((element)=>element.contains(target));
    }, [
        findAllFocusable,
        focusWithinRef
    ]);
    const interactive = computeInteractive(props);
    const focusMode = focusModeProp !== null && focusModeProp !== void 0 ? focusModeProp : interactive ? 'no-tab' : 'off';
    const groupperAttrs = (0, _reacttabster.useFocusableGroup)({
        tabBehavior: focusMap[focusMode]
    });
    const state = useCardBase_unstable({
        shouldRestrictTriggerAction,
        ...cardProps
    }, cardRef);
    // Apply focusable-group attributes only when the card is not selectable, not
    // disabled and the focus mode is enabled.
    const shouldApplyFocusAttributes = !disabled && !state.selectable && focusMode !== 'off';
    if (shouldApplyFocusAttributes) {
        Object.assign(state.root, groupperAttrs, {
            tabIndex: 0
        });
    }
    return {
        ...state,
        appearance,
        orientation,
        size
    };
};
const useCardBase_unstable = (props, ref)=>{
    const { disabled = false, ...restProps } = props;
    const [referenceId, setReferenceId] = _react.useState(_CardContext.cardContextDefaultValue.selectableA11yProps.referenceId);
    const [referenceLabel, setReferenceLabel] = _react.useState(_CardContext.cardContextDefaultValue.selectableA11yProps.referenceId);
    const { selectable, selected, selectableCardProps, selectFocused, checkboxSlot, floatingActionSlot } = (0, _useCardSelectable.useCardSelectable)(props, {
        referenceId,
        referenceLabel
    });
    const interactive = computeInteractive(props);
    let cardRootProps = {
        ...restProps,
        ...selectableCardProps
    };
    if (disabled) {
        cardRootProps = {
            ...restProps,
            'aria-disabled': true,
            onClick: undefined
        };
    }
    return {
        interactive,
        selectable,
        selectFocused,
        selected,
        disabled,
        selectableA11yProps: {
            setReferenceId,
            referenceId,
            referenceLabel,
            setReferenceLabel
        },
        components: {
            root: 'div',
            floatingAction: 'div',
            checkbox: 'input'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref,
            role: 'group',
            ...cardRootProps
        }), {
            elementType: 'div'
        }),
        floatingAction: floatingActionSlot,
        checkbox: checkboxSlot
    };
};
