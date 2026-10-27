# -------------------------------------------------------------------------
# Beam signal GPIO
# -------------------------------------------------------------------------
set_property PACKAGE_PIN G8       [get_ports {beam_active}]
set_property PACKAGE_PIN H8       [get_ports {beam_possible}]
set_property PACKAGE_PIN G7       [get_ports {clamp_request}]

set_property IOSTANDARD LVCMOS33  [get_ports {beam_active}]
set_property IOSTANDARD LVCMOS33  [get_ports {beam_possible}]
set_property IOSTANDARD LVCMOS33  [get_ports {clamp_request}]
