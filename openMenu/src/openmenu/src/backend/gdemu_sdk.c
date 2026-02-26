#include <kos.h>

#include "backend/gdemu_sdk.h"

#define ATA_SR_BSY           0x80
#define ATA_SR_DRDY          0x40
#define ATA_SR_DF            0x20
#define ATA_SR_DSC           0x10
#define ATA_SR_DRQ           0x08
#define ATA_SR_CORR          0x04
#define ATA_SR_IDX           0x02
#define ATA_SR_ERR           0x01

#define ATA_ER_BBK           0x80
#define ATA_ER_UNC           0x40
#define ATA_ER_MC            0x20
#define ATA_ER_IDNF          0x10
#define ATA_ER_MCR           0x08
#define ATA_ER_ABRT          0x04
#define ATA_ER_TK0NF         0x02
#define ATA_ER_AMNF          0x01

#define ATAPI_CMD_PACKET     0xA0

/* ATA-related registers; some addresses are shared between read and write paths */
#define G1_ATA_ALTSTATUS     0xA05F7018 /* Read */
#define G1_ATA_CTRL          0xA05F7018 /* Write */
#define G1_ATA_DATA          0xA05F7080 /* Read/Write */
#define G1_ATA_ERROR         0xA05F7084 /* Read */
#define G1_ATA_FEATURES      0xA05F7084 /* Write */
#define G1_ATA_IRQ_REASON    0xA05F7088 /* Read */
#define G1_ATA_SECTOR_COUNT  0xA05F7088 /* Write */
#define G1_ATA_LBA_LOW       0xA05F708C /* Read/Write */
#define G1_ATA_LBA_MID       0xA05F7090 /* Read/Write */
#define G1_ATA_LBA_HIGH      0xA05F7094 /* Read/Write */
#define G1_ATA_DEVICE_SELECT 0xA05F7098 /* Read/Write */
#define G1_ATA_STATUS_REG    0xA05F709C /* Read */
#define G1_ATA_COMMAND_REG   0xA05F709C /* Write */

/* status of the external interrupts
 * bit 3 = External Device interrupt
 * bit 2 = Modem interrupt
 * bit 1 = AICA interrupt
 * bit 0 = GD-ROM interrupt */
#define EXT_INT_STAT         0xA05F6904 /* Read */

/* Macros to access the ATA registers */
#define OUT16(addr, data)    *((volatile uint16_t*)addr) = data
#define OUT8(addr, data)     *((volatile uint8_t*)addr) = data
#define IN32(addr)           *((volatile uint32_t*)addr)
#define IN16(addr)           *((volatile uint16_t*)addr)
#define IN8(addr)            *((volatile uint8_t*)addr)

#define g1_ata_wait_interrupt()                                                                                        \
    do {                                                                                                               \
    } while (!(IN32(EXT_INT_STAT) & 1));

#define g1_ata_wait_status(n)                                                                                          \
    do {                                                                                                               \
    } while ((IN8(G1_ATA_ALTSTATUS) & (n)))

#define g1_ata_wait_nstatus(n)                                                                                         \
    do {                                                                                                               \
    } while (!(IN8(G1_ATA_ALTSTATUS) & (n)))

#define g1_ata_wait_drq()    g1_ata_wait_nstatus(ATA_SR_DRQ)

#define g1_ata_wait_bsydrq() g1_ata_wait_status(ATA_SR_DRQ | ATA_SR_BSY)

static int
send_packet_command(uint16_t* cmd_buff) {
    g1_ata_wait_bsydrq();
    OUT8(G1_ATA_COMMAND_REG, ATAPI_CMD_PACKET);
    g1_ata_wait_drq();

    (void)IN32(G1_ATA_STATUS_REG);
    int i;
    for (i = 0; i < 6; i++) {
        OUT16(G1_ATA_DATA, cmd_buff[i]);
    }

    g1_ata_wait_interrupt();

    (void)IN32(G1_ATA_STATUS_REG);

    return (IN8(G1_ATA_ALTSTATUS) & ATA_SR_ERR);
}

static int
send_packet_data_command(uint16_t* cmd_buff, uint16_t* buffer, uint32_t* size) {
    g1_ata_wait_bsydrq();

    OUT8(G1_ATA_FEATURES, 0);
    OUT8(G1_ATA_COMMAND_REG, ATAPI_CMD_PACKET);
    g1_ata_wait_drq();

    (void)IN32(G1_ATA_STATUS_REG);
    int i;
    for (i = 0; i < 6; i++) {
        OUT16(G1_ATA_DATA, cmd_buff[i]);
    }

    g1_ata_wait_interrupt();

    if (!(IN8(G1_ATA_STATUS_REG) & ATA_SR_DRQ)) {
        if (size) {
            *size = 0;
        }

        return 0;
    }

    uint16_t len = (IN8(G1_ATA_LBA_MID) | (IN8(G1_ATA_LBA_HIGH) << 8));

    *size = (uint32_t)len;

    len >>= 1;

    (void)IN32(G1_ATA_STATUS_REG);
    for (i = 0; i < len; i++) {
        buffer[i] = IN16(G1_ATA_DATA);
    }

    g1_ata_wait_interrupt();

    (void)IN32(G1_ATA_STATUS_REG);

    return (IN8(G1_ATA_ALTSTATUS) & ATA_SR_ERR);
}

/* return 8 byte: 00 00 09 01 00 00 14 05
 * 01 09 00 00 - internal bootloader version. don't used in early models
 * 05 14 00 00 - FW version (5.14.0)
 */

int
gdemu_get_version(void* buffer, uint32_t* size) {
    uint8_t cmd_buff[12] __attribute__((aligned(4)));
    ((uint32_t*)cmd_buff)[0] = 0;
    ((uint32_t*)cmd_buff)[1] = 0;
    ((uint32_t*)cmd_buff)[2] = 0;

    cmd_buff[0] = 0x52;

    return send_packet_data_command((uint16_t*)cmd_buff, (uint16_t*)buffer, size);
}

/* param = 0x55 next img */
/* param = 0x44 prev img */
int
gdemu_img_cmd(uint8_t cmd) {
    uint8_t cmd_buff[12] __attribute__((aligned(4)));
    ((uint32_t*)cmd_buff)[0] = 0;
    ((uint32_t*)cmd_buff)[1] = 0;
    ((uint32_t*)cmd_buff)[2] = 0;

    cmd_buff[0] = 0x52;
    cmd_buff[1] = 0x81;
    cmd_buff[2] = cmd;

    return send_packet_command((uint16_t*)cmd_buff);
}

/* 0 = reset to default img */
/* 1 to 999 = set image index */
int
gdemu_set_img_num(uint16_t img_num) {
    uint8_t cmd_buff[12] __attribute__((aligned(4)));
    ((uint32_t*)cmd_buff)[0] = 0;
    ((uint32_t*)cmd_buff)[1] = 0;
    ((uint32_t*)cmd_buff)[2] = 0;

    cmd_buff[0] = 0x52;
    cmd_buff[1] = 0x82;
    cmd_buff[2] = (uint8_t)(img_num);
    cmd_buff[3] = (uint8_t)(img_num >> 8);

    return send_packet_command((uint16_t*)cmd_buff);
}
