# -------------------------------------------------------------------------
# Beam signal GPIO
# -------------------------------------------------------------------------
set_property PACKAGE_PIN G8 [get_ports {beam_signal[0]}]
set_property IOSTANDARD LVCMOS33 [get_ports {beam_signal[0]}]

#######################################################################
#   User GPIO LEDs  (PL bank 88, LVCMOS33, active-high)
#   Verified against part0_pins.xml (GPIO_LED_0..3_LS).
#######################################################################
set_property PACKAGE_PIN D5 [get_ports {led_0[0]}]
set_property PACKAGE_PIN D6 [get_ports {led_0[1]}]
set_property PACKAGE_PIN A5 [get_ports {led_0[2]}]
set_property PACKAGE_PIN B5 [get_ports {led_0[3]}]
set_property IOSTANDARD LVCMOS33 [get_ports {led_0[*]}]

