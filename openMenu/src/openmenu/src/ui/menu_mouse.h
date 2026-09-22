#pragma once

#include <openmenu_settings.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "ui/common.h"
#include "ui/mouse_scrollbar.h"

void menu_mouse_begin(enum draw_state owner);
void menu_mouse_end(void);
void menu_mouse_invalidate(void);
bool handle_mouse_menu(enum control* input);

bool menu_mouse_collecting(void);
bool menu_mouse_active(void);
enum draw_state menu_mouse_owner(void);
bool menu_mouse_busy(enum draw_state owner);
void menu_mouse_surface(int x, int y, int width, int height);
void menu_mouse_row(int x, int y, int width, int height, int* cursor, int row, enum control action);
void menu_mouse_scroll(int* cursor, int* offset, int count, int window, const int* rows);
void menu_mouse_scrollbar(const mouse_scrollbar_t* bar);
int menu_mouse_window_y(int y, int height);
uint32_t menu_mouse_hash(uint32_t hash, const void* data, size_t size);
uint32_t menu_mouse_signature(enum draw_state owner);
bool menu_mouse_read(enum control* input, bool busy, bool cancel, int** selected);
