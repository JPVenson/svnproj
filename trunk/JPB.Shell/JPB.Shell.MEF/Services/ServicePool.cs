﻿#region Jean-Pierre Bachmann

// Erstellt von Jean-Pierre Bachmann am 10:32

#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JPB.Shell.Contracts.Interfaces;
using JPB.Shell.Contracts.Interfaces.Metadata;
using JPB.Shell.Contracts.Interfaces.Services;
using JPB.Shell.Contracts.Interfaces.Services.ApplicationServices;
using JPB.Shell.Contracts.Interfaces.Services.ModuleServices;
using JPB.Shell.Contracts.Interfaces.Services.ShellServices;
using JPB.Shell.MEF.Log;
using JPB.Shell.MEF.Log.Model;
using JPB.Shell.MEF.Model;
using JPB.Shell.MEF.Properties;

namespace JPB.Shell.MEF.Services
{
    public class ServicePoolFactory
    {
        private static bool Lock;

        public string PriorityKey { get; set; }
        public IEnumerable<string> SublookupPaths { get; set; }
        public bool IgnoreNonPkAsseambly { get; set; }
        public IApplicationContext ApplicationContext { get; set; }
        public bool WatchSublookupPaths { get; set; }
        public bool WithCheckForDupAsseamblys { get; set; }

        /// <summary>
        /// Factory Method to create a Valid instance of <see cref="ServicePool"/>
        /// To get a Valid Instance you are FORCED to set all propertys 
        /// Req propertys:
        ///     SublookupPaths
        /// Opt propertys:
        ///     PriorityKey
        ///     IgnoreNonPkAsseambly
        ///     ApplicationContext
        ///     WatchSublookupPaths
        ///     WithCheckForDupAsseamblys
        /// </summary>
        /// <returns>The new or a valid object</returns>
        public ServicePool CreateServicePool()
        {
            if (Lock)
                return null;

            Lock = true;

            if (ServicePool.Instance != null)
                return ServicePool.Instance;

            if (string.IsNullOrEmpty(PriorityKey))
                IgnoreNonPkAsseambly = false;

            if (SublookupPaths == null)
                throw new ArgumentNullException("SublookupPaths");

            if (!SublookupPaths.Any())
                throw new ArgumentNullException("SublookupPaths");

            var pool = new ServicePool(PriorityKey, SublookupPaths, IgnoreNonPkAsseambly, WithCheckForDupAsseamblys, WatchSublookupPaths);

            ServicePool.Instance = pool;

            ServicePool.ApplicationContainer = ApplicationContext ??
                                               new ApplicationContext(ImportPool.Instance, MessageBroker.Instance, pool, DataBroker.Instance, VisualModuleManager.Instance);

            pool.InitLoading();

            Lock = false;

            return pool;
        }

        internal ServicePool CreateDefaulPoolServicePool()
        {
            PriorityKey = string.Empty;
            SublookupPaths = new List<string> { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) };
            return CreateServicePool();
        }
    }

    public class ServicePool : IServicePool
    {
        /// <summary>
        /// DoNotUSE
        /// Use the ServicePoolFactory instedt
        /// </summary>
        /// <param name="priorityKey"></param>
        /// <param name="sublookuppaths"></param>
        /// <param name="ignoreNonPkAsseambly"></param>
        /// <param name="ignoreNonPkAssam"></param>
        /// <param name="watchSublookupPaths"></param>
        internal ServicePool(string priorityKey, IEnumerable<string> sublookuppaths, bool withCheckForDupAsseamblys, bool ignoreNonPkAsseambly, bool watchSublookupPaths)
        {
            _strongNameCatalog = new StrongNameCatalog(sublookuppaths, withCheckForDupAsseamblys, watchSublookupPaths);
            _strongNameCatalog.IgnoreAllNonPkAsseamblys = ignoreNonPkAsseambly;
            _strongNameCatalog.PriorityKey = priorityKey;
            _strongNameCatalog.AsyncInit();
            Container = new CompositionContainer(_strongNameCatalog, false);
        }

        public static ServicePoolFactory CreateFactory()
        {
            return new ServicePoolFactory();
        }

        /// <summary>
        ///     Used to get all <see cref="IApplicationProvider" /> in all exports to Provide a Initial Loading
        /// </summary>
        internal void InitLoading()
        {
            var serviceInternal = GetServiceInternal(false);
            var enumerable =
                serviceInternal.Where(s => s.Metadata.Contracts.Any(f => f == typeof(IApplicationProvider))).ToList();
            var asyncservices =
                enumerable.Where(s => !s.Metadata.ForceSynchronism).ToList();

            foreach (var asyncservice in asyncservices.OrderBy(s => s.Metadata.Priority))
            {
                try
                {
                    var asyncservice1 = asyncservice;
                    Task.Factory.StartNew(() => asyncservice1.Value.OnStart(ApplicationContainer));
                }
                catch (Exception ex)
                {
                    if (ApplicationContainer.ImportPool != null)
                        ApplicationContainer.ImportPool.LogEntries.Add(
                            new LogEntry(
                                string.Format("Error on startup of module: {0}", asyncservice.Metadata.Descriptor),
                                new Dictionary<string, object> { { "Exeption", ex } }));
                }
            }

            foreach (var val in enumerable.Except(asyncservices).OrderBy(s => s.Metadata.Priority))
            {
                try
                {
                    val.Value.OnStart(ApplicationContainer);
                }
                catch (Exception ex)
                {
                    if (ApplicationContainer.ImportPool != null)
                        ApplicationContainer.ImportPool.LogEntries.Add(
                            new LogEntry(string.Format("Error on startup of module: {0}", val.Metadata.Descriptor),
                                         new Dictionary<string, object> { { "Exeption", ex } }));
                }
            }
        }

        #region Propertys

        #region Container property

        /// <summary>
        ///     The Internal MEF Container.
        ///     This code is Internal and should be not used from your code!
        /// </summary>
        internal CompositionContainer Container { get; set; }

        #endregion

        #endregion

        #region Static's

        /// <summary>
        ///     The Defauld Application Container
        ///     This is used from ALL <see cref="IService" /> to comunicate with the <see cref="ServicePool" />
        /// </summary>
        public static IApplicationContext ApplicationContainer { get; set; }

        /// <summary>
        ///     The Current <see cref="IServicePool" /> Instance
        /// </summary>
        public static ServicePool Instance
        {
            get { return _instance ?? (_instance = new ServicePoolFactory().CreateServicePool()); }
            internal set { _instance = value; }
        }

        #endregion

        #region Default Functions

        public void FreeAssambly(Assembly assambly)
        {
            _strongNameCatalog.FreeAssambly(assambly);
        }

        /// <summary>
        ///     Register a Handler for the internal <see cref="Container" />
        /// </summary>
        /// <param name="exportsChange">Your Eventhandler</param>
        /// <returns>True if the Operation was Successful</returns>
        public bool RegisterExportsChanged(EventHandler<ExportsChangeEventArgs> exportsChange)
        {
            try
            {
                Container.ExportsChanged += exportsChange;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Register a Handler for the internal <see cref="Container" />
        /// </summary>
        /// <param name="exportsChangeing">Your Eventhandler</param>
        /// <returns>True if the Operation was Successful</returns>
        public bool RegisterExportsChanging(EventHandler<ExportsChangeEventArgs> exportsChangeing)
        {
            try
            {
                Container.ExportsChanging += exportsChangeing;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Import object that are marked with the Export Attrebute into your <paramref name="part" /> where your class container Import Flags
        ///     <a
        ///         href="http://msdn.microsoft.com/en-us/library/system.componentmodel.composition.importattribute%28v=vs.100%29.aspx">
        ///         ImportAttribute
        ///     </a>
        /// </summary>
        /// <param name="part">
        ///     your <paramref name="part" /> item
        /// </param>
        /// <returns>True if the Operation was Successful</returns>
        public bool Import([NotNull] ComposablePart part)
        {
            try
            {
                Container.SatisfyImportsOnce(part);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Compose your <paramref name="composition" /> with the current <see cref="Container" />
        /// </summary>
        /// <param name="composition">
        ///     Your <see cref="CompositionBatch" />
        /// </param>
        /// <returns>True if the Operation was Successful</returns>
        public bool Compose([Required] CompositionBatch composition)
        {
            try
            {
                Container.Compose(composition);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Gets the Service where the <see cref="IServiceMetadata.IsDefauldService" /> property is true
        ///     If more than one Service match this condition, this Function will call the <see cref="IIncidentFixerService" /> Service
        /// </summary>
        /// <typeparam name="T">Your Service interface</typeparam>
        /// <returns>
        ///     The found <typeparamref name="T" />
        /// </returns>
        /// <exception cref="NotImplementedException">
        ///     Thrown when There is no Unique <typeparamref name="T" /> and no Incident Fixer for <typeparamref name="T" />
        /// </exception>
        public T GetDefaultSingelService<T>() where T : class, IService
        {
            IEnumerable<Lazy<IService, IServiceMetadata>> exports = GetServiceInternal(false);
            IEnumerable<Lazy<IService, IServiceMetadata>> defaultservice = exports
                .Where(m => m.Metadata.IsDefauldService && m.Metadata.Contracts.Any(f => f == typeof(T)));

            Lazy<IService, IServiceMetadata>[] defauldInplementations =
                defaultservice as Lazy<IService, IServiceMetadata>[] ?? defaultservice.ToArray();

            if (defauldInplementations.Count() == 1)
            {
                var value = defauldInplementations.First().Value as T;

                if (value == default(T))
                {
                    ExeptionHelperThrowNoWrongParameterEx<T>(defauldInplementations.First().Value);
                }

                value.OnStart(ApplicationContainer);
                return value;
            }
            else if (_strongNameCatalog.WaitForCompletition != null)
            {
                _strongNameCatalog.WaitForCompletition.WaitOne();
                return GetDefaultSingelService<T>();
            }

            if (IsIncident || !defauldInplementations.Any())
                ExeptionHelperThrowNoInplementationFoundEx<T>();

            IsIncident = true;

            var rightservice = GetDefaultSingelService<IIncidentFixerService>();

            if (rightservice != null)
            {
                var service = rightservice.OnIncident(defauldInplementations);

                if (service == null)
                    ExeptionHelperThrowNoInplementationFoundEx<T>();

                IsIncident = false;
                var value = (T)service.Value;
                value.OnStart(ApplicationContainer);
                return value;
            }

            IsIncident = false;
            ExeptionHelperThrowNoInplementationFoundEx<T>();
            return null;
        }

        /// <summary>
        ///     Gets the first <typeparamref name="T" />
        ///     Ignores all else
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        /// </summary>
        /// <typeparam name="T">Your Service interface</typeparam>
        /// <returns>
        ///     <typeparamref name="T" />
        /// </returns>
        public T GetSingelService<T>() where T : class, IService
        {
            T firstOrDefault =
                GetServiceInternal()
                    .Where(m => m.Metadata.Contracts.Any(f => f == typeof(T)))
                    .Select(m => m.Value as T)
                    .FirstOrDefault();
            if (firstOrDefault != null)
                firstOrDefault.OnStart(ApplicationContainer);
            return firstOrDefault;
        }

        /// <summary>
        ///     Gets the first <typeparamref name="T" />
        ///     Ignores all else
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        /// </summary>
        /// <typeparam name="T">Your Service interface</typeparam>
        /// <param name="output">
        ///     The found <typeparamref name="T" /> will be Marshaled back to you
        /// </param>
        /// <returns>True if the Operation was Successful</returns>
        public bool TryGetSingelService<T>([Out] [Required] T output) where T : class, IService
        {
            output = default(T);
            try
            {
                output = GetSingelService<T>();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Get all Services that match <typeparamref name="T" />
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        /// </summary>
        /// <typeparam name="T">Your Service interface</typeparam>
        /// <returns>All found Services</returns>
        public IEnumerable<T> GetServices<T>() where T : class, IService
        {
            return GetServiceInternal()
                .Where(m => m.Metadata.Contracts.Any(f => f == typeof(T)))
                .Select(s => s.Value as T);
        }

        /// <summary>
        ///     Get all <typeparamref name="T" />
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        /// </summary>
        /// <typeparam name="T">Your Service interface</typeparam>
        /// <param name="output">
        ///     The found <typeparamref name="T" /> will be Marshaled back to you
        /// </param>
        /// <returns>True if the Operation was Successful</returns>
        public bool TryGetServices<T>([Out] [Required] IEnumerable<T> output) where T : class, IService
        {
            output = default(IEnumerable<T>);
            try
            {
                output = GetServices<T>();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Gets all Metadata
        /// </summary>
        /// <param name="output">The found Metadata will be Marshaled back to you</param>
        /// <returns>True if the Operation was Successful</returns>
        public bool TryGetMetadata([Out] [Required] IEnumerable<IServiceMetadata> output)
        {
            output = default(IEnumerable<IServiceMetadata>);
            try
            {
                output = GetMetadatas();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Gets all Metadata of type <typeparamref name="T" />
        /// </summary>
        /// <typeparam name="T">Your ServiceMetadata</typeparam>
        /// <returns>All found metadata</returns>
        public IEnumerable<T> GetMetadatas<T>() where T : class, IServiceMetadata
        {
            IEnumerable<Lazy<IService, T>> export = Container.GetExports<IService, T>();
            return export.Where(s => !s.Metadata.IsDefauldService).Select(s => s.Metadata);
        }

        /// <summary>
        ///     Gets all Metadata of type <see cref="IServiceMetadata" />
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        /// </summary>
        /// <returns>All found IServiceMetadata</returns>
        public IEnumerable<IServiceMetadata> GetMetadatas()
        {
            return GetServiceInternal().Select(s => s.Metadata);
        }

        /// <summary>
        ///     Gets all Metadata of type <see cref="IServiceMetadata" />
        /// </summary>
        /// <returns>All found IServiceMetadata</returns>
        public IEnumerable<IServiceMetadata> GetAllMetadata()
        {
            return GetServiceInternal(false).Select(s => s.Metadata);
        }

        /// <summary>
        ///     The General method to get Services without any kind of Filterin
        ///     This Code is Internal and should not be used directly from your code
        /// </summary>
        /// <param name="ignoreDefauld">
        ///     Ignore all services where the <see cref="IServiceMetadata.IsDefauldService" /> property true is
        /// </param>
        /// <returns>All found Services that match the Condition</returns>
        public IEnumerable<Lazy<IService, IServiceMetadata>> GetServiceInternal(bool ignoreDefauld = true)
        {
            if (!ignoreDefauld)
            {
                return Container
                    .GetExports<IService, IServiceMetadata>();
            }

            return Container
                .GetExports<IService, IServiceMetadata>()
                .Where(s => !s.Metadata.IsDefauldService);
        }

        // This Code is private and should not be used directly from your code
        /// <summary>
        /// </summary>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        private static void ExeptionHelperThrowNoWrongParameterEx<T>(IService value)
        {
            throw new ArgumentException("There is no service that inherts from: \r\n" +
                                        typeof(T) +
                                        "\r\nplease check the requested service\r\n" +
                                        value.GetType().ToString() +
                                        "\r\nparameter" +
                                        "\r\n See Data")
            {
                Data = { { "Type", typeof(T) } }
            };
        }

        // This Code is private and should not be used directly from your code
        /// <summary>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static void ExeptionHelperThrowNoInplementationFoundEx<T>()
        {
            throw new NotImplementedException("There is no unique inmplementation of the Service with the type : \r\n" +
                                              typeof(T) +
                                              "\r\nThe program can not Iditify one Module and tried to load the service but\r\n it allso does not found only one Defauld inplementation of the DefauldIncentFixerService.\r\n\r\n See Data")
                {
                    Data = { { "Type", typeof(T) } }
                };
        }

        /// <summary>
        ///     Gets all Metadata of type <see cref="IServiceMetadata" />
        ///     Ignores all <see cref="IServiceMetadata.IsDefauldService" />
        ///     Ignores all non instantiated
        /// </summary>
        /// <returns>All found IServiceMetadata</returns>
        public IServiceMetadata GetMetadata(IService service)
        {
            return
                GetServiceInternal()
                    .Where(s => s.IsValueCreated && s.Value == service)
                    .Select(s => s.Metadata)
                    .FirstOrDefault();
        }

        #endregion

        #region Member

        private static ServicePool _instance;

        private readonly StrongNameCatalog _strongNameCatalog = default(StrongNameCatalog);

        /// <summary>
        ///     Used to Prevend the ServicePool for an StackOverflow
        ///     This Code is Internal and should not be used directly from your code
        ///     <seealso cref="GetDefaultSingelService{T}" />
        /// </summary>
        internal bool IsIncident = false;

        #endregion
    }
}















//internal static ServicePool CreateParamServicePool(string priorityKey, bool ignoreNonPKAssam, params string[] subPaths)
//{
//    var pool = new ServicePool(priorityKey, subPaths, ignoreNonPKAssam);
//    Instance = pool;
//    if (ApplicationContainer == null)
//        ApplicationContainer = new ApplicationContext(ImportPool.Instance, MessageBroker.Instance, pool, DataBroker.Instance, VisualModuleManager.Instance);
//    pool.InitLoading();
//    return pool;
//}

//internal static ServicePool CreateParamServicePool(string priorityKey, bool ignoreNonPKAssam)
//{
//    return CreateParamServicePool(priorityKey, ignoreNonPKAssam,
//        new[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) });
//}

//internal static ServicePool CreateServicePool()
//{
//    return CreateParamServicePool(string.Empty, false);
//}

///// <summary>
///// Create the instance and set the PK for AsseamblyEnumeration
///// </summary>
///// <param name="priorityKey">PK for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/>)</param>
///// <param name="ignoreNonPkAsseamblys">Set to true to Ignore all Asseamblys that has not the <param name="priorityKey"/> in its name</param>
//public static void PreLoadServicePool(string priorityKey, bool ignoreNonPkAsseamblys)
//{
//    Instance = CreateParamServicePool(priorityKey, ignoreNonPkAsseamblys);
//}

///// <summary>
///// Create the instance and set the PK for AsseamblyEnumeration
///// </summary>
///// <param name="priorityKey">PK for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/>)</param>
///// <param name="ignoreNonPkAsseamblys">Set to true to Ignore all Asseamblys that has not the <param name="priorityKey"/> in its name</param>
///// <param name="sublookuppaths">Paths for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/></param>
//public static void PreLoadServicePool(string priorityKey, bool ignoreNonPkAsseamblys, params string[] sublookuppaths)
//{
//    Instance = CreateParamServicePool(priorityKey, ignoreNonPkAsseamblys, sublookuppaths);
//}

///// <summary>
///// Create the instance and set the PK for AsseamblyEnumeration
///// </summary>
///// <param name="priorityKey">PK for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/>)</param>
//public static void PreLoadServicePool(string priorityKey)
//{
//    Instance = CreateParamServicePool(priorityKey, false);
//}

///// <summary>
///// Create the instance and set the PK for AsseamblyEnumeration
///// </summary>
///// <param name="priorityKey">PK for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/>)</param>
///// <param name="sublookuppaths">Paths for Enumeration (see dokumentaion of <see cref="StrongNameCatalog"/></param>
//public static void PreLoadServicePool(string priorityKey, params string[] sublookuppaths)
//{
//    Instance = CreateParamServicePool(priorityKey, false, sublookuppaths);
//}