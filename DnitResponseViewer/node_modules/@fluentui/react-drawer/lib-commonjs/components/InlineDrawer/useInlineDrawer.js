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
    useInlineDrawerBase_unstable: function() {
        return useInlineDrawerBase_unstable;
    },
    useInlineDrawer_unstable: function() {
        return useInlineDrawer_unstable;
    }
});
const _interop_require_wildcard = require("@swc/helpers/_/_interop_require_wildcard");
const _react = /*#__PURE__*/ _interop_require_wildcard._(require("react"));
const _reactmotion = require("@fluentui/react-motion");
const _reactutilities = require("@fluentui/react-utilities");
const _reactsharedcontexts = require("@fluentui/react-shared-contexts");
const _drawerMotions = require("../../shared/drawerMotions");
const _useDrawerDefaultProps = require("../../shared/useDrawerDefaultProps");
const STATIC_MOTION = {
    active: true,
    canRender: true,
    ref: /*#__PURE__*/ _react.createRef(),
    type: 'idle'
};
const useInlineDrawer_unstable = (props, ref)=>{
    const { size, position, open, unmountOnClose } = (0, _useDrawerDefaultProps.useDrawerDefaultProps)(props);
    const { separator = false, surfaceMotion, ...baseProps } = props;
    const { dir } = (0, _reactsharedcontexts.useFluent_unstable)();
    const [animationDirection, setAnimationDirection] = _react.useState(open ? 'enter' : 'exit');
    const baseState = useInlineDrawerBase_unstable(baseProps, ref);
    return {
        ...baseState,
        components: {
            // eslint-disable-next-line @typescript-eslint/no-deprecated
            ...baseState.components,
            // casting from internal type that has required properties
            // to external type that only has optional properties
            // converting to unknown first as both Function component signatures are not compatible
            surfaceMotion: _drawerMotions.InlineDrawerMotion
        },
        size,
        separator,
        animationDirection,
        surfaceMotion: (0, _reactmotion.presenceMotionSlot)(surfaceMotion, {
            elementType: _drawerMotions.InlineDrawerMotion,
            defaultProps: {
                position,
                size,
                dir,
                visible: open,
                appear: unmountOnClose,
                unmountOnExit: unmountOnClose,
                onMotionFinish: (_, { direction })=>setAnimationDirection(direction),
                onMotionStart: (_, { direction })=>{
                    if (direction === 'enter') {
                        setAnimationDirection('enter');
                    }
                }
            }
        }),
        // Deprecated props
        motion: STATIC_MOTION
    };
};
const useInlineDrawerBase_unstable = (props, ref)=>{
    const { position, open, unmountOnClose } = (0, _useDrawerDefaultProps.useDrawerDefaultProps)(props);
    return {
        components: {
            root: 'div'
        },
        root: _reactutilities.slot.always((0, _reactutilities.getIntrinsicElementProps)('div', {
            ...props,
            ref,
            'aria-hidden': !unmountOnClose && !open ? true : undefined
        }), {
            elementType: 'div'
        }),
        open,
        position,
        unmountOnClose
    };
};
