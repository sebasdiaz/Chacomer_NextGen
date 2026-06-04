'use client';
import * as React from 'react';
import { useFluent_unstable as useFluent } from '@fluentui/react-shared-contexts';
import { ChevronRightFilled, ChevronRightRegular, ChevronLeftFilled, ChevronLeftRegular, bundleIcon } from '@fluentui/react-icons';
import { useMenuItemBase_unstable } from './useMenuItemBase';
const ChevronRightIcon = bundleIcon(ChevronRightFilled, ChevronRightRegular);
const ChevronLeftIcon = bundleIcon(ChevronLeftFilled, ChevronLeftRegular);
/**
 * Returns the props and state required to render the component
 */ export const useMenuItem_unstable = (props, ref)=>{
    const { dir } = useFluent();
    const state = useMenuItemBase_unstable(props, ref);
    // Set default chevron icon
    if (state.submenuIndicator) {
        var _state_submenuIndicator;
        var _children;
        (_children = (_state_submenuIndicator = state.submenuIndicator).children) !== null && _children !== void 0 ? _children : _state_submenuIndicator.children = dir === 'rtl' ? /*#__PURE__*/ React.createElement(ChevronLeftIcon, null) : /*#__PURE__*/ React.createElement(ChevronRightIcon, null);
    }
    return state;
};
