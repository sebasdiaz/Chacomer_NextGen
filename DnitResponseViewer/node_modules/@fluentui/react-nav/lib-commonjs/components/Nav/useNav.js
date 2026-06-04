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
    useNavBase_unstable: function() {
        return useNavBase_unstable;
    },
    useNav_unstable: function() {
        return useNav_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactutilities = require("@fluentui/react-utilities");
/**
 * Initial value for the uncontrolled case of the list of open indexes
 */ function initializeUncontrolledOpenCategories({ defaultOpenCategories, multiple }) {
    if (defaultOpenCategories !== undefined) {
        if (Array.isArray(defaultOpenCategories)) {
            return multiple ? defaultOpenCategories : [
                defaultOpenCategories[0]
            ];
        }
        return [
            defaultOpenCategories
        ];
    }
    return undefined;
}
/**
 * Updates the list of open indexes based on an index that changes
 * @param value - the index that will change
 * @param previousOpenItems - list of current open indexes
 * @param multiple - if Nav supports open categories at the same time
 */ const updateOpenCategories = (value, previousOpenItems, multiple)=>{
    if (multiple) {
        if (previousOpenItems.includes(value)) {
            return previousOpenItems.filter((i)=>i !== value);
        } else {
            return [
                ...previousOpenItems,
                value
            ];
        }
    }
    return previousOpenItems[0] === value ? [] : [
        value
    ];
};
const useNav_unstable = (props, ref)=>{
    const { density = 'medium' } = props;
    const state = useNavBase_unstable(props, ref);
    return {
        ...state,
        density,
        tabbable: false
    };
};
const useNavBase_unstable = (props, ref)=>{
    const { onNavItemSelect, onNavCategoryItemToggle, multiple = true, openCategories: controlledOpenCategoryItems, selectedCategoryValue: controlledSelectedCategoryValue, selectedValue: controlledSelectedValue, defaultOpenCategories, defaultSelectedValue, defaultSelectedCategoryValue } = props;
    const innerRef = _react.useRef(null);
    const [openCategories, setOpenCategories] = (0, _reactutilities.useControllableState)({
        state: controlledOpenCategoryItems,
        defaultState: initializeUncontrolledOpenCategories({
            defaultOpenCategories,
            multiple
        }),
        initialState: []
    });
    const [selectedCategoryValue, setSelectedCategoryValue] = (0, _reactutilities.useControllableState)({
        state: controlledSelectedCategoryValue,
        defaultState: defaultSelectedCategoryValue,
        initialState: undefined
    });
    const [selectedValue, setSelectedValue] = (0, _reactutilities.useControllableState)({
        state: controlledSelectedValue,
        defaultState: defaultSelectedValue,
        initialState: undefined
    });
    // considered usePrevious, but it is sensitive to re-renders
    // this could cause the previous to move to current in the case where the navItem list re-renders.
    // these refs avoid getRegisteredNavItems changing when selectedValue changes and causing
    // renders for navItems that have not changed.
    const currentSelectedValue = _react.useRef(undefined);
    const previousSelectedValue = _react.useRef(undefined);
    const currentSelectedCategoryValue = _react.useRef(undefined);
    const previousSelectedCategoryValue = _react.useRef(undefined);
    // eslint-disable-next-line react-hooks/refs
    if (currentSelectedValue.current !== selectedValue) {
        // eslint-disable-next-line react-hooks/refs
        previousSelectedValue.current = currentSelectedValue.current;
        // eslint-disable-next-line react-hooks/refs
        currentSelectedValue.current = selectedValue;
    }
    // eslint-disable-next-line react-hooks/refs
    if (currentSelectedCategoryValue.current !== selectedCategoryValue) {
        // eslint-disable-next-line react-hooks/refs
        previousSelectedCategoryValue.current = currentSelectedCategoryValue.current;
        // eslint-disable-next-line react-hooks/refs
        currentSelectedCategoryValue.current = selectedCategoryValue;
    }
    // used for NavItems and NavSubItems
    const onSelect = (0, _reactutilities.useEventCallback)((event, data)=>{
        setSelectedValue(data.value);
        setSelectedCategoryValue(data.categoryValue ? data.categoryValue : '');
        onNavItemSelect === null || onNavItemSelect === void 0 ? void 0 : onNavItemSelect(event, data);
    });
    // used for NavCategoryItems
    const onRequestNavCategoryItemToggle = (0, _reactutilities.useEventCallback)((event, data)=>{
        if (data.categoryValue !== undefined) {
            const nextOpenCategories = updateOpenCategories(data.categoryValue, openCategories !== null && openCategories !== void 0 ? openCategories : [], multiple);
            onNavCategoryItemToggle === null || onNavCategoryItemToggle === void 0 ? void 0 : onNavCategoryItemToggle(event, data);
            setOpenCategories(nextOpenCategories);
        }
    });
    const registeredNavItems = _react.useRef({});
    const onRegister = _react.useCallback((data)=>{
        registeredNavItems.current[JSON.stringify(data.value)] = data;
    }, []);
    const onUnregister = _react.useCallback((data)=>{
        delete registeredNavItems.current[JSON.stringify(data.value)];
    }, []);
    const getRegisteredNavItems = _react.useCallback(()=>{
        return {
            selectedValue: currentSelectedValue.current,
            previousSelectedValue: previousSelectedValue.current,
            selectedCategoryValue: currentSelectedCategoryValue.current,
            previousSelectedCategoryValue: previousSelectedCategoryValue.current,
            registeredNavItems: registeredNavItems.current
        };
    }, []);
    return {
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ref: (0, _reactutilities.useMergedRefs)(ref, innerRef),
            ...props
        }), {
            elementType: 'div'
        }),
        openCategories,
        selectedValue,
        selectedCategoryValue,
        onRegister,
        onUnregister,
        onSelect,
        getRegisteredNavItems,
        onRequestNavCategoryItemToggle,
        multiple
    };
};
