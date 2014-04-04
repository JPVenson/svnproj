#region Jean-Pierre Bachmann

// Erstellt von Jean-Pierre Bachmann am 15:50

#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using JPB.Shell.Contracts.Interfaces.Services.ApplicationServices;
using JPB.Shell.MEF.Services;

namespace JPB.Shell
{
    public class Program : Application
    {
        [STAThread]
        public static void Main(string[] param)
        {
            Main2();
        }

        [DebuggerStepThrough]
        public static void Main2()
        {
            var app = new Program();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var factory = ServicePool.CreateFactory();
            factory.SublookupPaths = new List<string> { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) };
            factory.PriorityKey = "JPB";
            factory.WithCheckForDupAsseamblys = true;
            factory.CreateServicePool();

            var defaultSingelService = ServicePool.Instance.GetDefaultSingelService<IApplicationContainer>();

            if (defaultSingelService == null)
                throw new CompositionException("Could not load the Default IVisualMainWindow");

            if (!defaultSingelService.OnEnter())
                Shutdown(1);

        }

        //[DebuggerStepThrough]
        protected override void OnExit(ExitEventArgs e)
        {
            var defaultSingelService = ServicePool.Instance.GetDefaultSingelService<IApplicationContainer>();

            if (defaultSingelService == null)
                throw new CompositionException("Could not load the Default IVisualMainWindow");

            if (defaultSingelService.OnLeave())
                base.Shutdown();
        }
    }
}