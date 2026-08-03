#pragma once

struct gd_item;

/* Stores the launched game along with the view it came from. Returns 1 when
 * the saved state changed and wants writing out. */
int last_game_record(const struct gd_item* disc);

/* Marks the next UI setup as the one allowed to move the cursor */
void last_game_arm(void);

/* Navigates to wherever the remembered game lives and hands back the row it
 * ended up on, or -1 when there is nothing to go back to. Every setup calls
 * this, only the armed one does any work. */
int last_game_take_row(void);
