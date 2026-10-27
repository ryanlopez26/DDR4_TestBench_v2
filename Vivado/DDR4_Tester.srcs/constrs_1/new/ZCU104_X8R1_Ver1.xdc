# -------------------------------------------------------------------------
# ZCU104 PL-Side DDR4 SODIMM XDC Constraints (Rank 1, x8, 64-bit)
# Grouped by Signal Type
# -------------------------------------------------------------------------

# -------------------------------------------------------------------------
# Clocks
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AH11 [get_ports {ddr4_sdram_0_ck_t[0]}]
set_property PACKAGE_PIN AJ11 [get_ports {ddr4_sdram_0_ck_c[0]}]

# -------------------------------------------------------------------------
# Control & Command Signals
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AB13 [get_ports {ddr4_sdram_0_cke[0]}]
set_property PACKAGE_PIN AD12 [get_ports {ddr4_sdram_0_cs_n[0]}]
set_property PACKAGE_PIN AF10 [get_ports {ddr4_sdram_0_odt[0]}]
set_property PACKAGE_PIN AD14 [get_ports ddr4_sdram_0_act_n]
set_property PACKAGE_PIN AF12 [get_ports ddr4_sdram_0_reset_n]
set_property PACKAGE_PIN AC13 [get_ports ddr4_sdram_0_parity]

# -------------------------------------------------------------------------
# Address Pins (A14=WE_B, A15=CAS_B, A16=RAS_B multiplexed in MIG)
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AH8  [get_ports {ddr4_sdram_0_adr[0]}]
set_property PACKAGE_PIN AG8  [get_ports {ddr4_sdram_0_adr[1]}]
set_property PACKAGE_PIN AF8  [get_ports {ddr4_sdram_0_adr[2]}]
set_property PACKAGE_PIN AG10 [get_ports {ddr4_sdram_0_adr[3]}]
set_property PACKAGE_PIN AG11 [get_ports {ddr4_sdram_0_adr[4]}]
set_property PACKAGE_PIN AH9  [get_ports {ddr4_sdram_0_adr[5]}]
set_property PACKAGE_PIN AG9  [get_ports {ddr4_sdram_0_adr[6]}]
set_property PACKAGE_PIN AH13 [get_ports {ddr4_sdram_0_adr[7]}]
set_property PACKAGE_PIN AK10 [get_ports {ddr4_sdram_0_adr[8]}]
set_property PACKAGE_PIN AJ10 [get_ports {ddr4_sdram_0_adr[9]}]
set_property PACKAGE_PIN AL8  [get_ports {ddr4_sdram_0_adr[10]}]
set_property PACKAGE_PIN AK8  [get_ports {ddr4_sdram_0_adr[11]}]
set_property PACKAGE_PIN AL12 [get_ports {ddr4_sdram_0_adr[12]}]
set_property PACKAGE_PIN AK12 [get_ports {ddr4_sdram_0_adr[13]}]
set_property PACKAGE_PIN AC12 [get_ports {ddr4_sdram_0_adr[14]}] 
set_property PACKAGE_PIN AE12 [get_ports {ddr4_sdram_0_adr[15]}] 
set_property PACKAGE_PIN AF11 [get_ports {ddr4_sdram_0_adr[16]}] 

# -------------------------------------------------------------------------
# Bank & Bank Group Pins
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AL10 [get_ports {ddr4_sdram_0_ba[0]}]
set_property PACKAGE_PIN AL11 [get_ports {ddr4_sdram_0_ba[1]}]
set_property PACKAGE_PIN AE14 [get_ports {ddr4_sdram_0_bg[0]}]
set_property PACKAGE_PIN AE13 [get_ports {ddr4_sdram_0_bgi[1]}]

# -------------------------------------------------------------------------
# Data Pins (DQ)
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AG14 [get_ports {ddr4_sdram_0_dq[0]}]
set_property PACKAGE_PIN AG15 [get_ports {ddr4_sdram_0_dq[1]}]
set_property PACKAGE_PIN AF15 [get_ports {ddr4_sdram_0_dq[2]}]
set_property PACKAGE_PIN AF16 [get_ports {ddr4_sdram_0_dq[3]}]
set_property PACKAGE_PIN AF17 [get_ports {ddr4_sdram_0_dq[4]}]
set_property PACKAGE_PIN AE17 [get_ports {ddr4_sdram_0_dq[5]}]
set_property PACKAGE_PIN AG18 [get_ports {ddr4_sdram_0_dq[6]}]
set_property PACKAGE_PIN AF18 [get_ports {ddr4_sdram_0_dq[7]}]
set_property PACKAGE_PIN AD16 [get_ports {ddr4_sdram_0_dq[8]}]
set_property PACKAGE_PIN AD17 [get_ports {ddr4_sdram_0_dq[9]}]
set_property PACKAGE_PIN AB14 [get_ports {ddr4_sdram_0_dq[10]}]
set_property PACKAGE_PIN AA14 [get_ports {ddr4_sdram_0_dq[11]}]
set_property PACKAGE_PIN AB15 [get_ports {ddr4_sdram_0_dq[12]}]
set_property PACKAGE_PIN AB16 [get_ports {ddr4_sdram_0_dq[13]}]
set_property PACKAGE_PIN AC16 [get_ports {ddr4_sdram_0_dq[14]}]
set_property PACKAGE_PIN AC17 [get_ports {ddr4_sdram_0_dq[15]}]
set_property PACKAGE_PIN AJ15 [get_ports {ddr4_sdram_0_dq[16]}]
set_property PACKAGE_PIN AJ16 [get_ports {ddr4_sdram_0_dq[17]}]
set_property PACKAGE_PIN AK17 [get_ports {ddr4_sdram_0_dq[18]}]
set_property PACKAGE_PIN AJ17 [get_ports {ddr4_sdram_0_dq[19]}]
set_property PACKAGE_PIN AL18 [get_ports {ddr4_sdram_0_dq[20]}]
set_property PACKAGE_PIN AK18 [get_ports {ddr4_sdram_0_dq[21]}]
set_property PACKAGE_PIN AL15 [get_ports {ddr4_sdram_0_dq[22]}]
set_property PACKAGE_PIN AL16 [get_ports {ddr4_sdram_0_dq[23]}]
set_property PACKAGE_PIN AN16 [get_ports {ddr4_sdram_0_dq[24]}]
set_property PACKAGE_PIN AN17 [get_ports {ddr4_sdram_0_dq[25]}]
set_property PACKAGE_PIN AP15 [get_ports {ddr4_sdram_0_dq[26]}]
set_property PACKAGE_PIN AP16 [get_ports {ddr4_sdram_0_dq[27]}]
set_property PACKAGE_PIN AN18 [get_ports {ddr4_sdram_0_dq[28]}]
set_property PACKAGE_PIN AM18 [get_ports {ddr4_sdram_0_dq[29]}]
set_property PACKAGE_PIN AP13 [get_ports {ddr4_sdram_0_dq[30]}]
set_property PACKAGE_PIN AN13 [get_ports {ddr4_sdram_0_dq[31]}]
set_property PACKAGE_PIN AA20 [get_ports {ddr4_sdram_0_dq[32]}]
set_property PACKAGE_PIN AA19 [get_ports {ddr4_sdram_0_dq[33]}]
set_property PACKAGE_PIN AD19 [get_ports {ddr4_sdram_0_dq[34]}]
set_property PACKAGE_PIN AC18 [get_ports {ddr4_sdram_0_dq[35]}]
set_property PACKAGE_PIN AE20 [get_ports {ddr4_sdram_0_dq[36]}]
set_property PACKAGE_PIN AD20 [get_ports {ddr4_sdram_0_dq[37]}]
set_property PACKAGE_PIN AC19 [get_ports {ddr4_sdram_0_dq[38]}]
set_property PACKAGE_PIN AB19 [get_ports {ddr4_sdram_0_dq[39]}]
set_property PACKAGE_PIN AE24 [get_ports {ddr4_sdram_0_dq[40]}]
set_property PACKAGE_PIN AE23 [get_ports {ddr4_sdram_0_dq[41]}]
set_property PACKAGE_PIN AF22 [get_ports {ddr4_sdram_0_dq[42]}]
set_property PACKAGE_PIN AF21 [get_ports {ddr4_sdram_0_dq[43]}]
set_property PACKAGE_PIN AG20 [get_ports {ddr4_sdram_0_dq[44]}]
set_property PACKAGE_PIN AG19 [get_ports {ddr4_sdram_0_dq[45]}]
set_property PACKAGE_PIN AH21 [get_ports {ddr4_sdram_0_dq[46]}]
set_property PACKAGE_PIN AG21 [get_ports {ddr4_sdram_0_dq[47]}]
set_property PACKAGE_PIN AJ22 [get_ports {ddr4_sdram_0_dq[48]}]
set_property PACKAGE_PIN AJ21 [get_ports {ddr4_sdram_0_dq[49]}]
set_property PACKAGE_PIN AK20 [get_ports {ddr4_sdram_0_dq[50]}]
set_property PACKAGE_PIN AJ20 [get_ports {ddr4_sdram_0_dq[51]}]
set_property PACKAGE_PIN AK19 [get_ports {ddr4_sdram_0_dq[52]}]
set_property PACKAGE_PIN AJ19 [get_ports {ddr4_sdram_0_dq[53]}]
set_property PACKAGE_PIN AL23 [get_ports {ddr4_sdram_0_dq[54]}]
set_property PACKAGE_PIN AL22 [get_ports {ddr4_sdram_0_dq[55]}]
set_property PACKAGE_PIN AN23 [get_ports {ddr4_sdram_0_dq[56]}]
set_property PACKAGE_PIN AM23 [get_ports {ddr4_sdram_0_dq[57]}]
set_property PACKAGE_PIN AP23 [get_ports {ddr4_sdram_0_dq[58]}]
set_property PACKAGE_PIN AN22 [get_ports {ddr4_sdram_0_dq[59]}]
set_property PACKAGE_PIN AP22 [get_ports {ddr4_sdram_0_dq[60]}]
set_property PACKAGE_PIN AP21 [get_ports {ddr4_sdram_0_dq[61]}]
set_property PACKAGE_PIN AN19 [get_ports {ddr4_sdram_0_dq[62]}]
set_property PACKAGE_PIN AM19 [get_ports {ddr4_sdram_0_dq[63]}]

# -------------------------------------------------------------------------
# Data Mask / Data Bus Inversion (DM_N)
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AH18 [get_ports {ddr4_sdram_0_dm_n[0]}]
set_property PACKAGE_PIN AD15 [get_ports {ddr4_sdram_0_dm_n[1]}]
set_property PACKAGE_PIN AM16 [get_ports {ddr4_sdram_0_dm_n[2]}]
set_property PACKAGE_PIN AP18 [get_ports {ddr4_sdram_0_dm_n[3]}]
set_property PACKAGE_PIN AE18 [get_ports {ddr4_sdram_0_dm_n[4]}]
set_property PACKAGE_PIN AH22 [get_ports {ddr4_sdram_0_dm_n[5]}]
set_property PACKAGE_PIN AL20 [get_ports {ddr4_sdram_0_dm_n[6]}]
set_property PACKAGE_PIN AP19 [get_ports {ddr4_sdram_0_dm_n[7]}]

# -------------------------------------------------------------------------
# Data Strobe True (DQS_T)
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AH14 [get_ports {ddr4_sdram_0_dqs_t[0]}]
set_property PACKAGE_PIN AA16 [get_ports {ddr4_sdram_0_dqs_t[1]}]
set_property PACKAGE_PIN AK15 [get_ports {ddr4_sdram_0_dqs_t[2]}]
set_property PACKAGE_PIN AM14 [get_ports {ddr4_sdram_0_dqs_t[3]}]
set_property PACKAGE_PIN AA18 [get_ports {ddr4_sdram_0_dqs_t[4]}]
set_property PACKAGE_PIN AF23 [get_ports {ddr4_sdram_0_dqs_t[5]}]
set_property PACKAGE_PIN AK22 [get_ports {ddr4_sdram_0_dqs_t[6]}]
set_property PACKAGE_PIN AM21 [get_ports {ddr4_sdram_0_dqs_t[7]}]

# -------------------------------------------------------------------------
# Data Strobe Complement (DQS_C)
# -------------------------------------------------------------------------
set_property PACKAGE_PIN AJ14 [get_ports {ddr4_sdram_0_dqs_c[0]}]
set_property PACKAGE_PIN AA15 [get_ports {ddr4_sdram_0_dqs_c[1]}]
set_property PACKAGE_PIN AK14 [get_ports {ddr4_sdram_0_dqs_c[2]}]
set_property PACKAGE_PIN AN14 [get_ports {ddr4_sdram_0_dqs_c[3]}]
set_property PACKAGE_PIN AB18 [get_ports {ddr4_sdram_0_dqs_c[4]}]
set_property PACKAGE_PIN AG23 [get_ports {ddr4_sdram_0_dqs_c[5]}]
set_property PACKAGE_PIN AK23 [get_ports {ddr4_sdram_0_dqs_c[6]}]
set_property PACKAGE_PIN AN21 [get_ports {ddr4_sdram_0_dqs_c[7]}]

#######################################################################
#   User GPIO LEDs  (PL bank 88, LVCMOS33, active-high)
#   Verified against part0_pins.xml (GPIO_LED_0..3_LS).
#######################################################################
set_property PACKAGE_PIN D5 [get_ports {led_0[0]}]   ;# GPIO_LED_0 / DS38
set_property PACKAGE_PIN D6 [get_ports {led_0[1]}]   ;# GPIO_LED_1 / DS37
set_property PACKAGE_PIN A5 [get_ports {led_0[2]}]   ;# GPIO_LED_2 / DS39
set_property PACKAGE_PIN B5 [get_ports {led_0[3]}]   ;# GPIO_LED_3 / DS40
set_property IOSTANDARD LVCMOS33 [get_ports {led_0[*]}]
