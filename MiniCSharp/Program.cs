using System;
using System.Windows.Forms;

// Asegúrate de que el namespace sea el mismo que el del resto de tu proyecto.
namespace MiniCSharp
{
    static class Program
    {
        /// <summary>
        ///  Punto de entrada principal para la aplicación.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Configuración estándar para aplicaciones de Windows Forms.
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Esta es la línea más importante:
            // Crea una nueva instancia de tu MainForm y la ejecuta, mostrando la ventana.
            Application.Run(new MainForm());
        }
    }
}