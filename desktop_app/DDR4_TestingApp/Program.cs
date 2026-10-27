namespace DDR4_TestingApp
{

    internal static class Program
    {

        // Current global status
        public static String taskName = "===";
        public static String taskInfo = "Welcome";
        public static float  taskProgress = 1.0f;


        // Selection and sampling sizes
        public static uint selection_size = 0;
        public static uint sample_size = 0;

        // Program.cs
        public static bool busy = false;   // true while a write/verify/dynamic/dump owns the connection

        [STAThread]
        static void Main()
        {
 
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            //Application.Run(new MainForm());
            Application.Run(new Launcher());

        }
    }
}