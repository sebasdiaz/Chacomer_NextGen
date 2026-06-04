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
    useMenuListBase_unstable: function() {
        return useMenuListBase_unstable;
    },
    useMenuList_unstable: function() {
        return useMenuList_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
const _reacttabster = require("@fluentui/react-tabster");
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _reactcontextselector = require("@fluentui/react-context-selector");
const _menuContext = require("../../contexts/menuContext");
const _useValidateNesting = require("../../utils/useValidateNesting");
const MENU_ITEM_ROLES = [
    'menuitem',
    'menuitemcheckbox',
    'menuitemradio'
];
const MENU_ITEM_ROLES_SELECTOR = MENU_ITEM_ROLES.map((role)=>`[role="${role}"]`).join(',');
const useMenuList_unstable = (props, ref)=>{
    const menuContext = useMenuContextSelectors();
    const hasMenuContext = (0, _reactcontextselector.useHasParentContext)(_menuContext.MenuContext);
    if (usingPropsAndMenuContext(props, menuContext, hasMenuContext)) {
        // TODO throw warnings in development safely
        // eslint-disable-next-line no-console
        console.warn('You are using both MenuList and Menu props, we recommend you to use Menu props when available');
    }
    const wrapperRef = _react.useRef(null);
    const { findAllFocusable } = (0, _reacttabster.useFocusFinders)();
    const { targetDocument } = (0, _reactsharedcontexts.useFluent_unstable)();
    const focusAttributes = (0, _reacttabster.useArrowNavigationGroup)({
        circular: true
    });
    const baseState = useMenuListBase_unstable(props, ref);
    // recreate root non-mutatively: merge wrapperRef so the effect below can
    // observe the rendered DOM element, and add Tabster arrow-nav attributes
    const mergedRootRef = (0, _reactutilities.useMergedRefs)(baseState.root.ref, wrapperRef);
    _react.useEffect(()=>{
        const element = wrapperRef.current;
        if (!hasMenuContext || !targetDocument || !element) {
            return;
        }
        const onTabsterMoveFocus = (e)=>{
            const nextElement = e.detail.next;
            if (nextElement && element.contains(targetDocument.activeElement) && !element.contains(nextElement)) {
                // Preventing Tabster from handling Tab press, useMenuPopover will handle it.
                e.preventDefault();
            }
        };
        targetDocument.addEventListener(_reacttabster.TabsterMoveFocusEventName, onTabsterMoveFocus);
        return ()=>{
            targetDocument.removeEventListener(_reacttabster.TabsterMoveFocusEventName, onTabsterMoveFocus);
        };
    }, [
        hasMenuContext,
        targetDocument
    ]);
    const setFocusByFirstCharacter = _react.useCallback((e, itemEl)=>{
        if (!wrapperRef.current) {
            return;
        }
        const menuItems = findAllFocusable(wrapperRef.current, (el)=>el.hasAttribute('role') && MENU_ITEM_ROLES.indexOf(el.getAttribute('role')) !== -1);
        focusItemMatchingFirstCharacter(menuItems, e.key, itemEl);
    }, [
        findAllFocusable
    ]);
    return {
        ...baseState,
        root: {
            ...focusAttributes,
            ...baseState.root,
            ref: mergedRootRef
        },
        setFocusByFirstCharacter
    };
};
const useMenuListBase_unstable = (props, ref)=>{
    const triggerId = (0, _menuContext.useMenuContext_unstable)((context)=>context.triggerId);
    const checkedValuesContext = (0, _menuContext.useMenuContext_unstable)((context)=>context.checkedValues);
    const onCheckedValueChangeContext = (0, _menuContext.useMenuContext_unstable)((context)=>context.onCheckedValueChange);
    const hasIconsContext = (0, _menuContext.useMenuContext_unstable)((context)=>context.hasIcons);
    const hasCheckmarksContext = (0, _menuContext.useMenuContext_unstable)((context)=>context.hasCheckmarks);
    const hasMenuContext = (0, _reactcontextselector.useHasParentContext)(_menuContext.MenuContext);
    const innerRef = _react.useRef(null);
    const validateNestingRef = (0, _useValidateNesting.useValidateNesting)('MenuList');
    const setFocusByFirstCharacter = _react.useCallback((e, itemEl)=>{
        if (!innerRef.current) {
            return;
        }
        const menuItems = Array.from(innerRef.current.querySelectorAll(MENU_ITEM_ROLES_SELECTOR));
        focusItemMatchingFirstCharacter(menuItems, e.key, itemEl);
    }, []);
    var _props_checkedValues;
    const [checkedValues, setCheckedValues] = (0, _reactutilities.useControllableState)({
        state: (_props_checkedValues = props.checkedValues) !== null && _props_checkedValues !== void 0 ? _props_checkedValues : hasMenuContext ? checkedValuesContext : undefined,
        defaultState: props.defaultCheckedValues,
        initialState: {}
    });
    var _props_onCheckedValueChange;
    const handleCheckedValueChange = (_props_onCheckedValueChange = props.onCheckedValueChange) !== null && _props_onCheckedValueChange !== void 0 ? _props_onCheckedValueChange : hasMenuContext ? onCheckedValueChangeContext : undefined;
    const toggleCheckbox = (0, _reactutilities.useEventCallback)((e, name, value, checked)=>{
        const checkedItems = (checkedValues === null || checkedValues === void 0 ? void 0 : checkedValues[name]) || [];
        const newCheckedItems = [
            ...checkedItems
        ];
        if (checked) {
            newCheckedItems.splice(newCheckedItems.indexOf(value), 1);
        } else {
            newCheckedItems.push(value);
        }
        handleCheckedValueChange === null || handleCheckedValueChange === void 0 ? void 0 : handleCheckedValueChange(e, {
            name,
            checkedItems: newCheckedItems
        });
        setCheckedValues((s)=>({
                ...s,
                [name]: newCheckedItems
            }));
    });
    const selectRadio = (0, _reactutilities.useEventCallback)((e, name, value)=>{
        const newCheckedItems = [
            value
        ];
        setCheckedValues((s)=>({
                ...s,
                [name]: newCheckedItems
            }));
        handleCheckedValueChange === null || handleCheckedValueChange === void 0 ? void 0 : handleCheckedValueChange(e, {
            name,
            checkedItems: newCheckedItems
        });
    });
    var _props_hasIcons, _ref, _props_hasCheckmarks, _ref1;
    return {
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            // FIXME:
            // `ref` is wrongly assigned to be `HTMLElement` instead of `HTMLDivElement`
            // but since it would be a breaking change to fix it, we are casting ref to it's proper type
            ref: (0, _reactutilities.useMergedRefs)(ref, innerRef, validateNestingRef),
            role: 'menu',
            'aria-labelledby': triggerId,
            ...props
        }), {
            elementType: 'div'
        }),
        checkedValues,
        hasIcons: (_ref = (_props_hasIcons = props.hasIcons) !== null && _props_hasIcons !== void 0 ? _props_hasIcons : hasIconsContext) !== null && _ref !== void 0 ? _ref : false,
        hasCheckmarks: (_ref1 = (_props_hasCheckmarks = props.hasCheckmarks) !== null && _props_hasCheckmarks !== void 0 ? _props_hasCheckmarks : hasCheckmarksContext) !== null && _ref1 !== void 0 ? _ref1 : false,
        hasMenuContext,
        setFocusByFirstCharacter,
        selectRadio,
        toggleCheckbox
    };
};
/**
 * Focuses the next menu item whose textContent starts with the typed character,
 * wrapping around the list. Shared between the Tabster-free base impl and the
 * Tabster-aware wrapper.
 */ const focusItemMatchingFirstCharacter = (menuItems, key, current)=>{
    let startIndex = menuItems.indexOf(current) + 1;
    if (startIndex === menuItems.length) {
        startIndex = 0;
    }
    const firstChars = menuItems.map((menuItem)=>{
        var _menuItem_textContent;
        return (_menuItem_textContent = menuItem.textContent) === null || _menuItem_textContent === void 0 ? void 0 : _menuItem_textContent.charAt(0).toLowerCase();
    });
    const char = key.toLowerCase();
    const getIndexFirstChars = (start)=>{
        for(let i = start; i < firstChars.length; i++){
            if (char === firstChars[i]) {
                return i;
            }
        }
        return -1;
    };
    let index = getIndexFirstChars(startIndex);
    if (index === -1) {
        index = getIndexFirstChars(0);
    }
    if (index > -1) {
        menuItems[index].focus();
    }
};
/**
 * Adds some sugar to fetching multiple context selector values
 */ const useMenuContextSelectors = ()=>{
    const checkedValues = (0, _menuContext.useMenuContext_unstable)((context)=>context.checkedValues);
    const onCheckedValueChange = (0, _menuContext.useMenuContext_unstable)((context)=>context.onCheckedValueChange);
    const triggerId = (0, _menuContext.useMenuContext_unstable)((context)=>context.triggerId);
    const hasIcons = (0, _menuContext.useMenuContext_unstable)((context)=>context.hasIcons);
    const hasCheckmarks = (0, _menuContext.useMenuContext_unstable)((context)=>context.hasCheckmarks);
    return {
        checkedValues,
        onCheckedValueChange,
        triggerId,
        hasIcons,
        hasCheckmarks
    };
};
/**
 * Helper function to detect if props and MenuContext values are both used
 */ const usingPropsAndMenuContext = (props, contextValue, hasMenuContext)=>{
    let isUsingPropsAndContext = false;
    for(const val in contextValue){
        if (props[val]) {
            isUsingPropsAndContext = true;
        }
    }
    return hasMenuContext && isUsingPropsAndContext;
};
