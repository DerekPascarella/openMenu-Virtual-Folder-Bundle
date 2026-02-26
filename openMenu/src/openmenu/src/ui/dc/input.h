#ifndef INPUT_H
#define INPUT_H
/*
 * Filename: d:\Dev\Dreamcast\UB_SHARE\gamejam\game\src\common\input.h
 * Path: d:\Dev\Dreamcast\UB_SHARE\gamejam\game\src\common
 * Created Date: Saturday, July 6th 2019, 6:23:18 pm
 * Author: Hayden Kowalchuk
 *
 * Copyright (c) 2019 HaydenKow
 */

#include <stdbool.h>
#include <stdint.h>

typedef enum {
    BTN_NULL = 0,
    BTN_A = 1,
    BTN_B = 2,
    BTN_X = 4,
    BTN_Y = 8,
    BTN_START = 16,
} BUTTON;

typedef enum {
    ACTION_NULL = 0,
    BTN_RELEASE = 32,
    BTN_PRESS = 64,
    BTN_HELD = 128,
} ACTION_TYPE;

typedef enum {
    DPAD_UP = 1,
    DPAD_DOWN = 2,
    DPAD_LEFT = 4,
    DPAD_RIGHT = 8,
    DPAD_UP_HELD = 16,
    DPAD_DOWN_HELD = 32,
    DPAD_LEFT_HELD = 64,
    DPAD_RIGHT_HELD = 128,
} DPAD_DIRECTION;

typedef enum {
    SHIFT_UP = 0,
    SHIFT_DOWN = 1,
    SHIFT_LEFT = 2,
    SHIFT_RIGHT = 3,
} DPAD_SHIFT;

typedef enum {
    AXES_NULL = 0,
    AXES_X = 1,
    AXES_Y = 2,
} ANALOG_AXES;

typedef enum {
    TRIGGER_NULL = 0,
    TRIGGER_L = 1,
    TRIGGER_R = 2,
} TRIGGER;

typedef uint8_t dpad_t;

/* Maximum number of simultaneous keyboard keys (hardware limit) */
#define INPT_MAX_KEYBOARD_KEYS 6

typedef struct __attribute__((packed)) inputs {
    uint8_t btn_a;
    uint8_t btn_b;
    uint8_t btn_x;
    uint8_t btn_y;
    uint8_t btn_start;
    uint8_t trg_left;  /* Pressure Sensitive */
    uint8_t trg_right; /* Pressure Sensitive */
    dpad_t dpad;
    uint8_t axes_1; /* Main Analog X */
    uint8_t axes_2; /* Main Analog Y */
                    /* Note the below may not exist on some platforms */
    uint8_t axes_3; /* Secondary Analog X */
    uint8_t axes_4; /* Secondary Analog Y */
    /* Keyboard support - list of currently pressed key scancodes */
    uint8_t kbd_modifiers;
    uint8_t kbd_buttons[INPT_MAX_KEYBOARD_KEYS];
} inputs;

void INPT_ReceiveFromHost(inputs _in);

bool INPT_Button(BUTTON btn);
bool INPT_ButtonEx(BUTTON btn, ACTION_TYPE type);
dpad_t INPT_DPAD(void);
bool INPT_DPADDirection(DPAD_DIRECTION dir);
float INPT_AnalogF(ANALOG_AXES axes);
uint8_t INPT_AnalogI(ANALOG_AXES axes);
bool INPT_TriggerPressed(TRIGGER trigger);
uint8_t INPT_TriggerValue(TRIGGER trigger);

/* Keyboard support */
bool INPT_KeyboardNone(void);
bool INPT_KeyboardButton(uint8_t kbtn);
bool INPT_KeyboardButtonPress(uint8_t kbtn);
uint8_t INPT_KeyboardModifiers(void);

/* Input Strings */
#ifdef _arch_dreamcast
#define STRING_A_BTN     "\x092"
#define STRING_B_BTN     "\x093"
#define STRING_X_BTN     "\x094"
#define STRING_Y_BTN     "\x095"
#define STRING_START_BTN "\x090"
#endif
#if defined(WIN32) || defined(__linux__)
#if 0
#define STRING_A_BTN     "V"
#define STRING_B_BTN     "C"
#define STRING_X_BTN     "X"
#define STRING_Y_BTN     "Z"
#define STRING_START_BTN "/"
#endif
#define STRING_A_BTN     "\x092"
#define STRING_B_BTN     "\x093"
#define STRING_X_BTN     "\x094"
#define STRING_Y_BTN     "\x095"
#define STRING_START_BTN "\x090"
#endif
#ifdef PSP
#define STRING_A_BTN     "\x096"
#define STRING_B_BTN     "\x097"
#define STRING_X_BTN     "\x098"
#define STRING_Y_BTN     "\x099"
#define STRING_START_BTN "\x091"
#endif

#endif /* INPUT_H */
