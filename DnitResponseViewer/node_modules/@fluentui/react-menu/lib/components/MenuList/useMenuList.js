'use client';
import * as React from 'react';
import { useMergedRefs, useEventCallback, useControllableState, getIntrinsicElementProps, slot } from '@fluentui/react-utilities';
import { useArrowNavigationGroup, useFocusFinders, TabsterMoveFocusEventName } from '@fluentui/react-tabster';
import { useFluent_unstable as useFluent } from '@fluentui/react-shared-contexts';
import { useHasParentContext } from '@fluentui/react-context-selector';
import { useMenuContext_unstable } from '../../contexts/menuContext';
import { MenuContext } from '../../contexts/menuContext';
import { useValidateNesting } from '../../utils/useValidateNesting';
const MENU_ITEM_ROLES = [
    'menuitem',
    'menuitemcheckbox',
    'menuitemradio'
];
const MENU_ITEM_ROLES_SELECTOR = MENU_ITEM_ROLES.map((role)=>`[role="${role}"]`).join(',');
/**
 * Returns the props and state required to render the component.
 *
 * Composes with `useMenuListBase_unstable` and adds Tabster-driven keyboard
 * navigation: circular arrow-key focus, a `TabsterMoveFocusEvent` listener
 * that lets `useMenuPopover_unstable` handle Tab key presses, a focus-aware
 * `setFocusByFirstCharacter`, and the `hasIcons` / `hasCheckmarks` slot
 * alignment hints sourced from the parent `MenuContext`.
 */ export const useMenuList_unstable = (props, ref)=>{
    const menuContext = useMenuContextSelectors();
    const hasMenuContext = useHasParentContext(MenuContext);
    if (usingPropsAndMenuContext(props, menuContext, hasMenuContext)) {
        // TODO throw warnings in development safely
        // eslint-disable-next-line no-console
        console.warn('You are using both MenuList and Menu props, we recommend you to use Menu props when available');
    }
    const wrapperRef = React.useRef(null);
    const { findAllFocusable } = useFocusFinders();
    const { targetDocument } = useFluent();
    const focusAttributes = useArrowNavigationGroup({
        circular: true
    });
    const baseState = useMenuListBase_unstable(props, ref);
    // recreate root non-mutatively: merge wrapperRef so the effect below can
    // observe the rendered DOM element, and add Tabster arrow-nav attributes
    const mergedRootRef = useMergedRefs(baseState.root.ref, wrapperRef);
    React.useEffect(()=>{
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
        targetDocument.addEventListener(TabsterMoveFocusEventName, onTabsterMoveFocus);
        return ()=>{
            targetDocument.removeEventListener(TabsterMoveFocusEventName, onTabsterMoveFocus);
        };
    }, [
        hasMenuContext,
        targetDocument
    ]);
    const setFocusByFirstCharacter = React.useCallback((e, itemEl)=>{
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
/**
 * Base hook for MenuList component, produces state required to render the component.
 *
 * Does not invoke any Tabster APIs internally: arrow-key navigation and the
 * focus-aware `setFocusByFirstCharacter` are added by the wrapper
 * `useMenuList_unstable`. The base's `setFocusByFirstCharacter` walks the DOM
 * via `querySelectorAll` and does not filter by Tabster's focusability rules,
 * so consumers integrating their own focus management should layer that on top.
 *
 * @param props - props from this instance of MenuList
 * @param ref - reference to root HTMLElement of MenuList
 */ export const useMenuListBase_unstable = (props, ref)=>{
    const triggerId = useMenuContext_unstable((context)=>context.triggerId);
    const checkedValuesContext = useMenuContext_unstable((context)=>context.checkedValues);
    const onCheckedValueChangeContext = useMenuContext_unstable((context)=>context.onCheckedValueChange);
    const hasIconsContext = useMenuContext_unstable((context)=>context.hasIcons);
    const hasCheckmarksContext = useMenuContext_unstable((context)=>context.hasCheckmarks);
    const hasMenuContext = useHasParentContext(MenuContext);
    const innerRef = React.useRef(null);
    const validateNestingRef = useValidateNesting('MenuList');
    const setFocusByFirstCharacter = React.useCallback((e, itemEl)=>{
        if (!innerRef.current) {
            return;
        }
        const menuItems = Array.from(innerRef.current.querySelectorAll(MENU_ITEM_ROLES_SELECTOR));
        focusItemMatchingFirstCharacter(menuItems, e.key, itemEl);
    }, []);
    var _props_checkedValues;
    const [checkedValues, setCheckedValues] = useControllableState({
        state: (_props_checkedValues = props.checkedValues) !== null && _props_checkedValues !== void 0 ? _props_checkedValues : hasMenuContext ? checkedValuesContext : undefined,
        defaultState: props.defaultCheckedValues,
        initialState: {}
    });
    var _props_onCheckedValueChange;
    const handleCheckedValueChange = (_props_onCheckedValueChange = props.onCheckedValueChange) !== null && _props_onCheckedValueChange !== void 0 ? _props_onCheckedValueChange : hasMenuContext ? onCheckedValueChangeContext : undefined;
    const toggleCheckbox = useEventCallback((e, name, value, checked)=>{
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
    const selectRadio = useEventCallback((e, name, value)=>{
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
        root: slot.always(getIntrinsicElementProps('div', {
            // FIXME:
            // `ref` is wrongly assigned to be `HTMLElement` instead of `HTMLDivElement`
            // but since it would be a breaking change to fix it, we are casting ref to it's proper type
            ref: useMergedRefs(ref, innerRef, validateNestingRef),
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
    const checkedValues = useMenuContext_unstable((context)=>context.checkedValues);
    const onCheckedValueChange = useMenuContext_unstable((context)=>context.onCheckedValueChange);
    const triggerId = useMenuContext_unstable((context)=>context.triggerId);
    const hasIcons = useMenuContext_unstable((context)=>context.hasIcons);
    const hasCheckmarks = useMenuContext_unstable((context)=>context.hasCheckmarks);
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
