#pragma once

#include <stdbool.h>
#include <stdint.h>

typedef struct {
    int x, y, width, height;
    int thumb_y, thumb_height, inset;
    int maximum, page;
} mouse_scrollbar_t;

void mouse_scrollbar_cancel(void);
void mouse_scrollbar_rebase(uint32_t identity);
bool mouse_scrollbar_read(const mouse_scrollbar_t* bar, uint32_t identity, int* offset);
