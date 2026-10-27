-- debug_leds.vhd
-- Four LED indicators for ZCU104 DDR4 bring-up.
--   led[0] = calibration complete (sync'd to pl_clk0 for a clean signal)
--   led[1] = ~1 Hz heartbeat on c0_ddr4_ui_clk -- proves MIG MMCM is running
--   led[2] = ~0.75 Hz heartbeat on pl_clk0     -- proves PL clock domain is alive
--   led[3] = always on -- "power good" indicator
--
-- No AXI, no bus interfaces, no custom IP. Add via "Add Module" in the BD.

library ieee;
use ieee.std_logic_1164.all;
use ieee.numeric_std.all;

entity debug_leds is
    generic (
        HB_BIT_UI : integer := 27;   -- 2^27 / 266.5 MHz = ~0.50 s -> ~1.0 Hz blink
        HB_BIT_PL : integer := 26    -- 2^26 / 100   MHz = ~0.67 s -> ~0.75 Hz blink
    );
    port (
        clk_ui         : in  std_logic;   -- c0_ddr4_ui_clk  (~266.5 MHz)
        clk_pl         : in  std_logic;   -- pl_clk0         (~100   MHz)
        calib_complete : in  std_logic;   -- from ddr4_0/c0_init_calib_complete
        led            : out std_logic_vector(3 downto 0)
    );
end entity;

architecture rtl of debug_leds is
    signal calib_q1, calib_q2 : std_logic := '0';

    -- Mark these as async CDC FFs so Vivado packs them adjacent for
    -- minimum MTBF and exempts them from synchronous timing checks.
    attribute ASYNC_REG : string;
    attribute ASYNC_REG of calib_q1 : signal is "TRUE";
    attribute ASYNC_REG of calib_q2 : signal is "TRUE";

    signal cnt_ui : unsigned(HB_BIT_UI downto 0) := (others => '0');
    signal cnt_pl : unsigned(HB_BIT_PL downto 0) := (others => '0');
begin
    -- LED[0]: calibration complete, synchronized
    process(clk_pl)
    begin
        if rising_edge(clk_pl) then
            calib_q1 <= calib_complete;
            calib_q2 <= calib_q1;
        end if;
    end process;

    -- LED[1]: heartbeat on ui_clk
    --   If this LED stays dark, ui_clk isn't running -> MIG didn't come out of reset.
    process(clk_ui)
    begin
        if rising_edge(clk_ui) then
            cnt_ui <= cnt_ui + 1;
        end if;
    end process;

    -- LED[2]: heartbeat on pl_clk0
    --   Should always blink once the PS releases pl_resetn0. If LED[2] blinks but
    --   LED[1] doesn't, the deadlock is specifically in the MIG sys_rst path.
    process(clk_pl)
    begin
        if rising_edge(clk_pl) then
            cnt_pl <= cnt_pl + 1;
        end if;
    end process;

    led(0) <= calib_q2;
    led(1) <= std_logic(cnt_ui(HB_BIT_UI));
    led(2) <= std_logic(cnt_pl(HB_BIT_PL));
    led(3) <= '1';
end architecture;