/**
 * Checks whether the given element is a combobox option element.
 * Supports elements with role="option" or role="menuitemcheckbox".
 *
 * @param element - the element to check
 * @returns true if the element has a valid combobox option role, false otherwise
 */ "use strict";
Object.defineProperty(exports, "__esModule", {
    value: true
});
Object.defineProperty(exports, "isComboboxOptionElement", {
    enumerable: true,
    get: function() {
        return isComboboxOptionElement;
    }
});
function isComboboxOptionElement(element) {
    return element.role === 'option' || element.role === 'menuitemcheckbox';
}
