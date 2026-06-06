using Boxwright.App.Services;
using Boxwright.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Boxwright.App.ViewModels;

/// <summary>
/// View model for the main window. Hosts the VM list, the New-VM / Settings / "Get an OS"
/// catalog panes, and host context (the detected accelerator and the VMs folder) shown in
/// the status bar.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;
    private readonly IFolderOpener _folderOpener;
    private readonly IOsCatalogSource _catalogSource;
    private readonly IIsoDownloader _isoDownloader;
    private readonly ISeedGenerator _seedGenerator;
    private readonly IUnattendedInstallerResolver _installerResolver;
    private readonly IAutounattendSeedGenerator _autounattendSeedGenerator;
    private readonly IFilePicker _filePicker;
    private readonly IUiDispatcher _dispatcher;

    public MainWindowViewModel(
        VmListViewModel vms,
        AcceleratorDetector acceleratorDetector,
        VmRepository repository,
        IDiskService diskService,
        IFolderOpener folderOpener,
        IOsCatalogSource catalogSource,
        IIsoDownloader isoDownloader,
        ISeedGenerator seedGenerator,
        IUnattendedInstallerResolver installerResolver,
        IAutounattendSeedGenerator autounattendSeedGenerator,
        IFilePicker filePicker,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(vms);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        ArgumentNullException.ThrowIfNull(folderOpener);
        ArgumentNullException.ThrowIfNull(catalogSource);
        ArgumentNullException.ThrowIfNull(isoDownloader);
        ArgumentNullException.ThrowIfNull(seedGenerator);
        ArgumentNullException.ThrowIfNull(installerResolver);
        ArgumentNullException.ThrowIfNull(autounattendSeedGenerator);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Vms = vms;
        _repository = repository;
        _diskService = diskService;
        _folderOpener = folderOpener;
        _catalogSource = catalogSource;
        _isoDownloader = isoDownloader;
        _seedGenerator = seedGenerator;
        _installerResolver = installerResolver;
        _autounattendSeedGenerator = autounattendSeedGenerator;
        _filePicker = filePicker;
        _dispatcher = dispatcher;
        Accelerator = acceleratorDetector.Detect().ToQemuValue();
        VmsDirectory = repository.RootDirectory;
    }

    /// <summary>The VM list shown as the window's main content.</summary>
    public VmListViewModel Vms { get; }

    /// <summary>The application name (window title and toolbar heading).</summary>
    public string Title { get; } = "Boxwright";

    /// <summary>The acceleration backend detected for this host (kvm/hvf/whpx/tcg).</summary>
    public string Accelerator { get; }

    /// <summary>Where this machine's VMs are stored on disk.</summary>
    public string VmsDirectory { get; }

    /// <summary>The active New-VM form, or null when not creating.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreating), nameof(IsShowingForm))]
    private NewVmViewModel? _creation;

    /// <summary>The active Settings form, or null when not editing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsShowingForm))]
    private VmSettingsViewModel? _editing;

    /// <summary>The active "Get an OS" catalog pane, or null when not browsing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowsingCatalog), nameof(IsShowingForm))]
    private CatalogViewModel? _catalog;

    /// <summary>True while the New-VM form is shown.</summary>
    public bool IsCreating => Creation is not null;

    /// <summary>True while the Settings form is shown.</summary>
    public bool IsEditing => Editing is not null;

    /// <summary>True while the OS catalog pane is shown.</summary>
    public bool IsBrowsingCatalog => Catalog is not null;

    /// <summary>True while any pane occupies the right side (hides the detail/placeholder).</summary>
    public bool IsShowingForm => IsCreating || IsEditing || IsBrowsingCatalog;

    [RelayCommand]
    private void BeginCreate()
    {
        if (IsShowingForm)
        {
            return;
        }

        var form = new NewVmViewModel(_repository, _diskService, _filePicker, _autounattendSeedGenerator, _isoDownloader, _dispatcher, IsNameTaken);
        form.Created += OnVmCreated;
        form.Cancelled += OnCreateCancelled;
        Creation = form;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (IsShowingForm || Vms.SelectedVm is null)
        {
            return;
        }

        VmListItemViewModel target = Vms.SelectedVm;
        var form = new VmSettingsViewModel(
            target.Vm,
            _repository,
            name => IsNameTakenByOther(name, target.Vm.Config.Id),
            target.Status != VmStatus.Stopped);
        form.Saved += (_, config) => OnSettingsSaved(target, config);
        form.Cancelled += (_, _) => Editing = null;
        Editing = form;
    }

    [RelayCommand]
    private void BeginBrowseCatalog()
    {
        if (IsShowingForm)
        {
            return;
        }

        var form = new CatalogViewModel(_catalogSource, _isoDownloader, _repository, _diskService, _seedGenerator, _installerResolver, _dispatcher, IsNameTaken);
        form.Created += OnCatalogCreated;
        form.Cancelled += OnCatalogCancelled;
        Catalog = form;
        form.LoadEntriesCommand.Execute(null);
    }

    /// <summary>Reveals the app-wide logs folder (<c>boxwright.log</c>) in the OS file manager.</summary>
    [RelayCommand]
    private void OpenLogsFolder() => _folderOpener.OpenFolder(AppPaths.LogsDirectory);

    private bool IsNameTaken(string name) =>
        Vms.Vms.Any(i => string.Equals(i.Vm.Config.Name, name, StringComparison.OrdinalIgnoreCase));

    private bool IsNameTakenByOther(string name, string excludeId) =>
        Vms.Vms.Any(i =>
            !string.Equals(i.Vm.Config.Id, excludeId, StringComparison.Ordinal) &&
            string.Equals(i.Vm.Config.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnSettingsSaved(VmListItemViewModel target, VmConfig config)
    {
        target.ApplyConfig(config);
        Editing = null;
    }

    private void OnVmCreated(object? sender, Vm vm)
    {
        DetachCreation();
        Vms.AddCreated(vm);
    }

    private void OnCreateCancelled(object? sender, EventArgs e) => DetachCreation();

    private void DetachCreation()
    {
        if (Creation is null)
        {
            return;
        }

        Creation.Created -= OnVmCreated;
        Creation.Cancelled -= OnCreateCancelled;
        Creation.Dispose();
        Creation = null;
    }

    private void OnCatalogCreated(object? sender, Vm vm)
    {
        DetachCatalog();
        Vms.AddCreated(vm);
    }

    private void OnCatalogCancelled(object? sender, EventArgs e) => DetachCatalog();

    private void DetachCatalog()
    {
        if (Catalog is null)
        {
            return;
        }

        Catalog.Created -= OnCatalogCreated;
        Catalog.Cancelled -= OnCatalogCancelled;
        Catalog.Dispose();
        Catalog = null;
    }
}
