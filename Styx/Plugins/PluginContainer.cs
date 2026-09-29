using System;
using System.ComponentModel;
using Styx.Plugins.PluginClass;

namespace Styx.Plugins
{
	public class PluginContainer : INotifyPropertyChanged
	{
		private bool _enabled;

		public HBPlugin Plugin { get; private set; }

		public bool Enabled
		{
			get => _enabled;
			set
			{
				if (_enabled != value)
				{
					_enabled = value;
					
					if (_enabled)
					{
						try
						{
							Plugin.Initialize();
							Plugin.OnEnable();
						}
						catch (Exception ex)
						{
							// A partially initialized plugin must not remain eligible
							// for Pulse or prevent a later explicit enable retry.
							_enabled = false;
							try { Helpers.Logging.WriteException(ex); }
							finally { DisableAndDispose(); }
						}
					}
					else
					{
						DisableAndDispose();
					}
					
					// Notify the completed state, including a refused enable, so
					// bindings recover without observers interrupting cleanup.
					try { OnPropertyChanged(nameof(Enabled)); }
					finally
					{
						// No immediate save - HB 4.3.4 pattern.
						PluginManager.UpdateEnabledPlugins();
					}
				}
			}
		}

		private void DisableAndDispose()
		{
			try
			{
				try { Plugin.OnDisable(); }
				catch (Exception ex) { Helpers.Logging.WriteException(ex); }
			}
			finally
			{
				// OnDisable (or its diagnostic handler) cannot skip resource release.
				try { Plugin.Dispose(); }
				catch (Exception ex) { Helpers.Logging.WriteException(ex); }
			}
		}

		public string Name => Plugin.Name;
		public string Author => Plugin.Author;
		public Version Version => Plugin.Version;
		public bool WantButton => Plugin.WantButton;
		public string ButtonText => Plugin.ButtonText;

		public PluginContainer(HBPlugin plugin, bool enabled)
		{
			Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
			_enabled = false; // Don't trigger initialization yet
			Enabled = enabled; // Now trigger if needed
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		protected virtual void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
