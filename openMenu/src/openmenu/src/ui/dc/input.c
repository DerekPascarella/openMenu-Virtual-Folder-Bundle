/*
 * Filename: d:\Dev\Dreamcast\UB_SHARE\gamejam\game\src\common\input.c
 * Path: d:\Dev\Dreamcast\UB_SHARE\gamejam\game\src\common
 * Created Date: Saturday, July 6th 2019, 6:23:40 pm
 * Author: Hayden Kowalchuk
 *
 * Copyright (c) 2019 HaydenKow
 */
#include "input.h"

#include <string.h>

static inputs _current, _last;
static uint8_t _last_kbd_buttons[INPT_MAX_KEYBOARD_KEYS];

void
INPT_ReceiveFromHost(inputs _in) {
    memset(&_current, 0, sizeof(inputs));

    /* Handle Setting Single press */
    for (int index = 0; index < 5; index++) {
        uint8_t* state_in = (uint8_t*)(&_in.btn_a) + index;
        uint8_t* state_last = (uint8_t*)(&_last.btn_a) + index;
        uint8_t* state_current = (uint8_t*)(&_current.btn_a) + index;

        if (*state_in && !(*state_last)) {
            *state_current |= BTN_PRESS;
        }
        if (*state_in && *state_last) {
            *state_current |= BTN_HELD;
        }
        if (!*state_in && *state_last) {
            *state_current |= BTN_RELEASE;
        }
    }

    /* Handle DPAD Values */
    /* Loops through 8 values */
    for (int index = 0; index < 4; index++) {
        if ((_in.dpad & (1 << index))) {
            _current.dpad |= (1 << index);
        }

        if ((_in.dpad & (1 << index)) && (_last.dpad & (1 << index))) {
            _current.dpad |= (1 << (index + 4));
        }
    }

    /* Handle Analog Axes Values */
    /* Loops through 4 Axes for 2 Analog Sticks */
    _current.axes_1 = _in.axes_1;
    _current.axes_2 = _in.axes_2;

    /* Triggers */
    _current.trg_left = _in.trg_left;
    _current.trg_right = _in.trg_right;

    /* Keyboard - copy pressed key scancodes */
    _current.kbd_modifiers = _in.kbd_modifiers;
    for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
        _current.kbd_buttons[i] = _in.kbd_buttons[i];
    }

    memcpy(_last_kbd_buttons, _last.kbd_buttons, sizeof(_last_kbd_buttons));
    _last = _in;
}

bool
INPT_Button(BUTTON btn) {
    switch (btn) {
        case BTN_A: return _current.btn_a; break;
        case BTN_B: return _current.btn_b; break;
        case BTN_X: return _current.btn_x; break;
        case BTN_Y: return _current.btn_y; break;
        case BTN_START: return _current.btn_start; break;
        default:
            return _current.btn_a || _current.btn_b || _current.btn_x || _current.btn_y || _current.btn_start;
            break;
    }
    return false;
}

bool
INPT_ButtonEx(BUTTON btn, ACTION_TYPE type) {
    switch (btn) {
        case BTN_A: return (_current.btn_a & type) == type; break;
        case BTN_B: return (_current.btn_b & type) == type; break;
        case BTN_X: return (_current.btn_x & type) == type; break;
        case BTN_Y: return (_current.btn_y & type) == type; break;
        case BTN_START: return (_current.btn_start & type) == type; break;
        default: break;
    }
    return false;
}

dpad_t
INPT_DPAD() {
    return _current.dpad;
}

bool
INPT_DPADDirection(DPAD_DIRECTION dir) {
    return !!(_current.dpad & dir);
}

/* -1.0 is lowest, 1.0 is highest and middle is 0 */
float
INPT_AnalogF(ANALOG_AXES axes) {
    switch (axes) {
        case AXES_X: return ((int)(_current.axes_1) - 128) / 128.0f; break;
        case AXES_Y: return ((int)(_current.axes_2) - 128) / 128.0f; break;
        case AXES_NULL:
        default: break;
    }
    return 0.0f;
}

/* 0 is lowest, 255 is highest and middle is 128 */
uint8_t
INPT_AnalogI(ANALOG_AXES axes) {
    switch (axes) {
        case AXES_X: return _current.axes_1; break;
        case AXES_Y: return _current.axes_2; break;
        case AXES_NULL:
        default: break;
    }
    /* default center value */
    return 128;
}

bool
INPT_TriggerPressed(TRIGGER trigger) {
    switch (trigger) {
        case TRIGGER_L: return !!_current.trg_left; break;
        case TRIGGER_R: return !!_current.trg_right; break;
        case TRIGGER_NULL:
        default: break;
    }
    return false;
}

uint8_t
INPT_TriggerValue(TRIGGER trigger) {
    switch (trigger) {
        case TRIGGER_L: return _current.trg_left; break;
        case TRIGGER_R: return _current.trg_right; break;
        case TRIGGER_NULL:
        default: break;
    }
    return 0;
}

bool
INPT_KeyboardNone(void) {
    for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
        if (_current.kbd_buttons[i] != 0) {
            return false;
        }
    }
    return true;
}

bool
INPT_KeyboardButton(uint8_t kbtn) {
    /* Search for the scancode in the list of pressed keys */
    for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
        if (_current.kbd_buttons[i] == kbtn) {
            return true;
        }
    }
    return false;
}

bool
INPT_KeyboardButtonPress(uint8_t kbtn) {
    /* Edge detection: true only on the frame the key is first pressed */
    bool currently_pressed = false;
    for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
        if (_current.kbd_buttons[i] == kbtn) {
            currently_pressed = true;
            break;
        }
    }
    if (!currently_pressed) {
        return false;
    }
    for (int i = 0; i < INPT_MAX_KEYBOARD_KEYS; i++) {
        if (_last_kbd_buttons[i] == kbtn) {
            return false;
        }
    }
    return true;
}

uint8_t
INPT_KeyboardModifiers(void) {
    return _current.kbd_modifiers;
}
