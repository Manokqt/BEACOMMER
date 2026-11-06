
using BEACOMMER;
using System;
using System.Windows.Forms;

namespace BEACOMMER
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            Application.Run(new frmWebApp());
        }
    }
}
