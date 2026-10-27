# ========================================================================
# ZCU104 PL-Side DDR4 SODIMM  --  x16 devices, single rank, 64-bit
# Generated from part0_pins.xml (xczu7ev-ffvc1156-2-e) -- authoritative
#
# Replaces ZCU104_X16R1_Ver1.xdc, in which 116/116 DDR4 pin assignments
# were incorrect. Notably dm_n[0] was on AH18 = CLK_300_P.
#
# x8 -> x16 delta: only bg[1] is removed. x16 dies have BG0 only.
# The socket pinout is identical for x8- and x16-based modules.
# ========================================================================

# ------------------------------------------------------------------------
# 300 MHz reference clock (SODIMM refclk, bank 64)
# ------------------------------------------------------------------------
# Physical period 3.3333 ns. The DDR4 IP propagates 3.335 ns from its
# Reference Input Clock Speed field -- if the IP already creates this
# clock, DELETE the create_clock below rather than duplicating it.
# Verify with:  report_clocks ; get_clocks -of [get_ports c0_sys_clk_clk_p]
set_property PACKAGE_PIN AH18  [get_ports {clk_300mhz_clk_p}]
set_property PACKAGE_PIN AH17  [get_ports {clk_300mhz_clk_n}]

# ------------------------------------------------------------------------
# Memory clock
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AF18  [get_ports {ddr4_sdram_0_ck_t[0]}]
set_property PACKAGE_PIN AG18  [get_ports {ddr4_sdram_0_ck_c[0]}]

# ------------------------------------------------------------------------
# Control and command
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AD17  [get_ports {ddr4_sdram_0_cke[0]}]
set_property PACKAGE_PIN AA15  [get_ports {ddr4_sdram_0_cs_n[0]}]
set_property PACKAGE_PIN AE15  [get_ports {ddr4_sdram_0_odt[0]}]
set_property PACKAGE_PIN AC17  [get_ports {ddr4_sdram_0_act_n}]
set_property PACKAGE_PIN AB14  [get_ports {ddr4_sdram_0_reset_n}]

# NOTE: ddr4_sdram_0_parity REMOVED. part0_pins.xml defines no parity pin,
# and CA parity is disabled in your IP config so the port does not exist.
# Re-add only if you enable CA parity, and get the pin from UG1267 Tbl 3-4.

# ------------------------------------------------------------------------
# Address  (A14=WE_n, A15=CAS_n, A16=RAS_n multiplexed with ACT_n)
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AH16  [get_ports {ddr4_sdram_0_adr[0]}]
set_property PACKAGE_PIN AG14  [get_ports {ddr4_sdram_0_adr[1]}]
set_property PACKAGE_PIN AG15  [get_ports {ddr4_sdram_0_adr[2]}]
set_property PACKAGE_PIN AF15  [get_ports {ddr4_sdram_0_adr[3]}]
set_property PACKAGE_PIN AF16  [get_ports {ddr4_sdram_0_adr[4]}]
set_property PACKAGE_PIN AJ14  [get_ports {ddr4_sdram_0_adr[5]}]
set_property PACKAGE_PIN AH14  [get_ports {ddr4_sdram_0_adr[6]}]
set_property PACKAGE_PIN AF17  [get_ports {ddr4_sdram_0_adr[7]}]
set_property PACKAGE_PIN AK17  [get_ports {ddr4_sdram_0_adr[8]}]
set_property PACKAGE_PIN AJ17  [get_ports {ddr4_sdram_0_adr[9]}]
set_property PACKAGE_PIN AK14  [get_ports {ddr4_sdram_0_adr[10]}]
set_property PACKAGE_PIN AK15  [get_ports {ddr4_sdram_0_adr[11]}]
set_property PACKAGE_PIN AL18  [get_ports {ddr4_sdram_0_adr[12]}]
set_property PACKAGE_PIN AK18  [get_ports {ddr4_sdram_0_adr[13]}]
set_property PACKAGE_PIN AA16  [get_ports {ddr4_sdram_0_adr[14]}]
set_property PACKAGE_PIN AA14  [get_ports {ddr4_sdram_0_adr[15]}]
set_property PACKAGE_PIN AD15  [get_ports {ddr4_sdram_0_adr[16]}]

# ------------------------------------------------------------------------
# Bank and bank group  (x16: BG0 only -- bg[1] intentionally absent)
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AL15  [get_ports {ddr4_sdram_0_ba[0]}]
set_property PACKAGE_PIN AL16  [get_ports {ddr4_sdram_0_ba[1]}]
set_property PACKAGE_PIN AC16  [get_ports {ddr4_sdram_0_bg[0]}]

# bg[1] (c0_ddr4_bg1 = AB16) NOT assigned: x16 devices have no BG1.
# The original file had 'ddr4_sdram_0_bgi[1]' -- typo and wrong for x16.

# ------------------------------------------------------------------------
# Data  (DQ[15:0]=device 0, [31:16]=device 1, [47:32]=device 2, [63:48]=device 3)
# ------------------------------------------------------------------------
# --- byte 0: DQ[7:0], DQS0, DM0 ---
set_property PACKAGE_PIN AE24  [get_ports {ddr4_sdram_0_dq[0]}]
set_property PACKAGE_PIN AE23  [get_ports {ddr4_sdram_0_dq[1]}]
set_property PACKAGE_PIN AF22  [get_ports {ddr4_sdram_0_dq[2]}]
set_property PACKAGE_PIN AF21  [get_ports {ddr4_sdram_0_dq[3]}]
set_property PACKAGE_PIN AG20  [get_ports {ddr4_sdram_0_dq[4]}]
set_property PACKAGE_PIN AG19  [get_ports {ddr4_sdram_0_dq[5]}]
set_property PACKAGE_PIN AH21  [get_ports {ddr4_sdram_0_dq[6]}]
set_property PACKAGE_PIN AG21  [get_ports {ddr4_sdram_0_dq[7]}]
# --- byte 1: DQ[15:8], DQS1, DM1 ---
set_property PACKAGE_PIN AA20  [get_ports {ddr4_sdram_0_dq[8]}]
set_property PACKAGE_PIN AA19  [get_ports {ddr4_sdram_0_dq[9]}]
set_property PACKAGE_PIN AD19  [get_ports {ddr4_sdram_0_dq[10]}]
set_property PACKAGE_PIN AC18  [get_ports {ddr4_sdram_0_dq[11]}]
set_property PACKAGE_PIN AE20  [get_ports {ddr4_sdram_0_dq[12]}]
set_property PACKAGE_PIN AD20  [get_ports {ddr4_sdram_0_dq[13]}]
set_property PACKAGE_PIN AC19  [get_ports {ddr4_sdram_0_dq[14]}]
set_property PACKAGE_PIN AB19  [get_ports {ddr4_sdram_0_dq[15]}]
# --- byte 2: DQ[23:16], DQS2, DM2 ---
set_property PACKAGE_PIN AJ22  [get_ports {ddr4_sdram_0_dq[16]}]
set_property PACKAGE_PIN AJ21  [get_ports {ddr4_sdram_0_dq[17]}]
set_property PACKAGE_PIN AK20  [get_ports {ddr4_sdram_0_dq[18]}]
set_property PACKAGE_PIN AJ20  [get_ports {ddr4_sdram_0_dq[19]}]
set_property PACKAGE_PIN AK19  [get_ports {ddr4_sdram_0_dq[20]}]
set_property PACKAGE_PIN AJ19  [get_ports {ddr4_sdram_0_dq[21]}]
set_property PACKAGE_PIN AL23  [get_ports {ddr4_sdram_0_dq[22]}]
set_property PACKAGE_PIN AL22  [get_ports {ddr4_sdram_0_dq[23]}]
# --- byte 3: DQ[31:24], DQS3, DM3 ---
set_property PACKAGE_PIN AN23  [get_ports {ddr4_sdram_0_dq[24]}]
set_property PACKAGE_PIN AM23  [get_ports {ddr4_sdram_0_dq[25]}]
set_property PACKAGE_PIN AP23  [get_ports {ddr4_sdram_0_dq[26]}]
set_property PACKAGE_PIN AN22  [get_ports {ddr4_sdram_0_dq[27]}]
set_property PACKAGE_PIN AP22  [get_ports {ddr4_sdram_0_dq[28]}]
set_property PACKAGE_PIN AP21  [get_ports {ddr4_sdram_0_dq[29]}]
set_property PACKAGE_PIN AN19  [get_ports {ddr4_sdram_0_dq[30]}]
set_property PACKAGE_PIN AM19  [get_ports {ddr4_sdram_0_dq[31]}]
# --- byte 4: DQ[39:32], DQS4, DM4 ---
set_property PACKAGE_PIN AC13  [get_ports {ddr4_sdram_0_dq[32]}]
set_property PACKAGE_PIN AB13  [get_ports {ddr4_sdram_0_dq[33]}]
set_property PACKAGE_PIN AF12  [get_ports {ddr4_sdram_0_dq[34]}]
set_property PACKAGE_PIN AE12  [get_ports {ddr4_sdram_0_dq[35]}]
set_property PACKAGE_PIN AF13  [get_ports {ddr4_sdram_0_dq[36]}]
set_property PACKAGE_PIN AE13  [get_ports {ddr4_sdram_0_dq[37]}]
set_property PACKAGE_PIN AE14  [get_ports {ddr4_sdram_0_dq[38]}]
set_property PACKAGE_PIN AD14  [get_ports {ddr4_sdram_0_dq[39]}]
# --- byte 5: DQ[47:40], DQS5, DM5 ---
set_property PACKAGE_PIN AG8   [get_ports {ddr4_sdram_0_dq[40]}]
set_property PACKAGE_PIN AF8   [get_ports {ddr4_sdram_0_dq[41]}]
set_property PACKAGE_PIN AG10  [get_ports {ddr4_sdram_0_dq[42]}]
set_property PACKAGE_PIN AG11  [get_ports {ddr4_sdram_0_dq[43]}]
set_property PACKAGE_PIN AH13  [get_ports {ddr4_sdram_0_dq[44]}]
set_property PACKAGE_PIN AG13  [get_ports {ddr4_sdram_0_dq[45]}]
set_property PACKAGE_PIN AJ11  [get_ports {ddr4_sdram_0_dq[46]}]
set_property PACKAGE_PIN AH11  [get_ports {ddr4_sdram_0_dq[47]}]
# --- byte 6: DQ[55:48], DQS6, DM6 ---
set_property PACKAGE_PIN AK9   [get_ports {ddr4_sdram_0_dq[48]}]
set_property PACKAGE_PIN AJ9   [get_ports {ddr4_sdram_0_dq[49]}]
set_property PACKAGE_PIN AK10  [get_ports {ddr4_sdram_0_dq[50]}]
set_property PACKAGE_PIN AJ10  [get_ports {ddr4_sdram_0_dq[51]}]
set_property PACKAGE_PIN AL12  [get_ports {ddr4_sdram_0_dq[52]}]
set_property PACKAGE_PIN AK12  [get_ports {ddr4_sdram_0_dq[53]}]
set_property PACKAGE_PIN AL10  [get_ports {ddr4_sdram_0_dq[54]}]
set_property PACKAGE_PIN AL11  [get_ports {ddr4_sdram_0_dq[55]}]
# --- byte 7: DQ[63:56], DQS7, DM7 ---
set_property PACKAGE_PIN AM8   [get_ports {ddr4_sdram_0_dq[56]}]
set_property PACKAGE_PIN AM9   [get_ports {ddr4_sdram_0_dq[57]}]
set_property PACKAGE_PIN AM10  [get_ports {ddr4_sdram_0_dq[58]}]
set_property PACKAGE_PIN AM11  [get_ports {ddr4_sdram_0_dq[59]}]
set_property PACKAGE_PIN AP11  [get_ports {ddr4_sdram_0_dq[60]}]
set_property PACKAGE_PIN AN11  [get_ports {ddr4_sdram_0_dq[61]}]
set_property PACKAGE_PIN AP9   [get_ports {ddr4_sdram_0_dq[62]}]
set_property PACKAGE_PIN AP10  [get_ports {ddr4_sdram_0_dq[63]}]

# ------------------------------------------------------------------------
# Data mask / DBI
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AH22  [get_ports {ddr4_sdram_0_dm_n[0]}]
set_property PACKAGE_PIN AE18  [get_ports {ddr4_sdram_0_dm_n[1]}]
set_property PACKAGE_PIN AL20  [get_ports {ddr4_sdram_0_dm_n[2]}]
set_property PACKAGE_PIN AP19  [get_ports {ddr4_sdram_0_dm_n[3]}]
set_property PACKAGE_PIN AF11  [get_ports {ddr4_sdram_0_dm_n[4]}]
set_property PACKAGE_PIN AH12  [get_ports {ddr4_sdram_0_dm_n[5]}]
set_property PACKAGE_PIN AK13  [get_ports {ddr4_sdram_0_dm_n[6]}]
set_property PACKAGE_PIN AN12  [get_ports {ddr4_sdram_0_dm_n[7]}]

# ------------------------------------------------------------------------
# Data strobes
# ------------------------------------------------------------------------
set_property PACKAGE_PIN AF23  [get_ports {ddr4_sdram_0_dqs_t[0]}]
set_property PACKAGE_PIN AA18  [get_ports {ddr4_sdram_0_dqs_t[1]}]
set_property PACKAGE_PIN AK22  [get_ports {ddr4_sdram_0_dqs_t[2]}]
set_property PACKAGE_PIN AM21  [get_ports {ddr4_sdram_0_dqs_t[3]}]
set_property PACKAGE_PIN AC12  [get_ports {ddr4_sdram_0_dqs_t[4]}]
set_property PACKAGE_PIN AG9   [get_ports {ddr4_sdram_0_dqs_t[5]}]
set_property PACKAGE_PIN AK8   [get_ports {ddr4_sdram_0_dqs_t[6]}]
set_property PACKAGE_PIN AN9   [get_ports {ddr4_sdram_0_dqs_t[7]}]

set_property PACKAGE_PIN AG23  [get_ports {ddr4_sdram_0_dqs_c[0]}]
set_property PACKAGE_PIN AB18  [get_ports {ddr4_sdram_0_dqs_c[1]}]
set_property PACKAGE_PIN AK23  [get_ports {ddr4_sdram_0_dqs_c[2]}]
set_property PACKAGE_PIN AN21  [get_ports {ddr4_sdram_0_dqs_c[3]}]
set_property PACKAGE_PIN AD12  [get_ports {ddr4_sdram_0_dqs_c[4]}]
set_property PACKAGE_PIN AH9   [get_ports {ddr4_sdram_0_dqs_c[5]}]
set_property PACKAGE_PIN AL8   [get_ports {ddr4_sdram_0_dqs_c[6]}]
set_property PACKAGE_PIN AN8   [get_ports {ddr4_sdram_0_dqs_c[7]}]

# -------------------------------------------------------------------------
# Beam signal GPIO
# -------------------------------------------------------------------------
set_property PACKAGE_PIN G8       [get_ports {beam_signal[0]}]
set_property IOSTANDARD LVCMOS33  [get_ports {beam_signal[0]}]

#######################################################################
#   User GPIO LEDs  (PL bank 88, LVCMOS33, active-high)
#   Verified against part0_pins.xml (GPIO_LED_0..3_LS).
#######################################################################
set_property PACKAGE_PIN D5 [get_ports {led_0[0]}]   ;# GPIO_LED_0 / DS38
set_property PACKAGE_PIN D6 [get_ports {led_0[1]}]   ;# GPIO_LED_1 / DS37
set_property PACKAGE_PIN A5 [get_ports {led_0[2]}]   ;# GPIO_LED_2 / DS39
set_property PACKAGE_PIN B5 [get_ports {led_0[3]}]   ;# GPIO_LED_3 / DS40
set_property IOSTANDARD LVCMOS33 [get_ports {led_0[*]}]
