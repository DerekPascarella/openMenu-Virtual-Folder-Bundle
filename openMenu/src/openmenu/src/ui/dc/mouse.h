#ifndef OPENMENU_MOUSE_H
#define OPENMENU_MOUSE_H

#include <stdbool.h>
#include <stdint.h>

#define MOUSE_LEFT  0x04
#define MOUSE_RIGHT 0x02
#define MOUSE_THIRD 0x09

typedef struct {
    bool present;
    bool moved;
    bool changed;
    int x;
    int y;
    int wheel; /* Positive values scroll down. */
    uint8_t pressed;
    uint8_t buttons;
} mouse_frame_t;

typedef struct {
    int remainder;
    int speed;
} mouse_scroll_state_t;

const mouse_frame_t* mouse_get_state(void);
void mouse_poll(bool enabled);
void mouse_reset(void);
int mouse_scroll_steps(int wheel, mouse_scroll_state_t* state);
void mouse_draw_cursor(uint32_t fill, uint32_t outline);

#endif
