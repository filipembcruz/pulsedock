using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace PulseDock.Services
{
    /// <summary>
    /// UI languages (English by default, Portuguese optional).
    /// XAML:  Text="{Binding [Key], Source={x:Static svc:Loc.Instance}, Mode=OneWay}"
    /// Code:  Loc.T("Key")
    /// Switching the language updates everything immediately (PropertyChanged "Item[]" + Changed event).
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new();

        /// <summary>Raised after the language changes (for texts built in code).</summary>
        public static event Action? Changed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public static bool IsPortuguese { get; private set; }

        public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        public string this[string key] => T(key);

        /// <summary>"auto" follows the Windows language; "en" or "pt" force it.</summary>
        public static void Apply(string? setting)
        {
            bool pt = setting switch
            {
                "pt" => true,
                "en" => false,
                _ => string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "pt", StringComparison.OrdinalIgnoreCase)
            };
            IsPortuguese = pt;
            Culture = CultureInfo.GetCultureInfo(pt ? "pt-BR" : "en-US");
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
            Changed?.Invoke();
        }

        public static string T(string key)
        {
            if (Strings.TryGetValue(key, out var s)) return IsPortuguese ? s.pt : s.en;
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

        private static readonly Dictionary<string, (string en, string pt)> Strings = new()
        {
            // ---- General ----
            ["App"] = ("PulseDock", "PulseDock"),
            ["Menu"] = ("Menu", "Menu"),
            ["WinProcesses"] = ("Processes — PulseDock", "Processos — PulseDock"),
            ["WinMenu"] = ("Menu — PulseDock", "Menu — PulseDock"),
            ["WinSettings"] = ("Settings — PulseDock", "Configurações — PulseDock"),
            ["Close"] = ("Close", "Fechar"),
            ["CloseEsc"] = ("Close (Esc)", "Fechar (Esc)"),
            ["Cancel"] = ("Cancel", "Cancelar"),
            ["Save"] = ("Save", "Salvar"),
            ["Settings"] = ("Settings", "Configurações"),
            ["SettingsEllipsis"] = ("Settings…", "Configurações…"),
            ["Quit"] = ("Quit PulseDock", "Sair do PulseDock"),
            ["QuitShort"] = ("Quit", "Sair"),

            // ---- Metrics ----
            ["Cpu"] = ("CPU", "CPU"),
            ["Memory"] = ("Memory", "Memória"),
            ["MemShort"] = ("MEM", "MEM"),
            ["Disk"] = ("Disk", "Disco"),
            ["Network"] = ("Network", "Rede"),
            ["Integrated"] = ("· Integrated", "· Integrada"),
            ["GpuIdle"] = ("idle", "em repouso"),
            ["GpuIdleShort"] = ("idle", "repouso"),
            ["GpuIdleTip"] = ("idle; temperature is read only while it's in use, so PulseDock never wakes it up", "em repouso; a temperatura só é lida enquanto ela está em uso, para o PulseDock nunca acordá-la"),
            ["Dedicated"] = ("· Dedicated", "· Dedicada"),
            ["ReadWrite"] = ("R {0} · W {1} MB/s", "L {0} · E {1} MB/s"),
            ["ReadWriteShort"] = ("R{0} W{1}MB/s", "L{0} E{1}MB/s"),
            ["CpuTempNeedsAdmin"] = ("CPU temperature: enable Advanced sensors and run as administrator", "Temperatura da CPU: ative Sensores avançados e execute como administrador"),

            // ---- Widget ----
            ["ExpandFull"] = ("Open full view", "Abrir completo"),
            ["BackToCompact"] = ("Back to compact", "Voltar ao compacto"),
            ["PinOnTop"] = ("Keep on top of all windows", "Fixar sobre todas as janelas"),
            ["UpdatesEvery"] = ("Updates every {0} s", "Atualiza a cada {0} s"),
            ["FreePosition"] = ("Free position", "Posição livre"),
            ["Tray"] = ("Tray", "Bandeja"),

            // ---- Modes / corners ----
            ["ModeOverlay"] = ("Overlay", "Sobreposto"),
            ["ModeDesktop"] = ("Desktop", "Área de trabalho"),
            ["ModeTray"] = ("Tray", "Bandeja"),
            ["ModeDesktopLong"] = ("Desktop only", "Só na área de trabalho"),
            ["ModeTrayLong"] = ("System tray", "Bandeja do sistema"),
            ["ModeOverlayDesc"] = ("Always on top of every window, pinned to the chosen corner.", "Sempre acima de todas as janelas, fixo no canto escolhido."),
            ["ModeDesktopDesc"] = ("Stays behind your windows; visible when the desktop is showing.", "Fica atrás das janelas; aparece quando a área de trabalho está visível."),
            ["ModeTrayDesc"] = ("Opens from the icon under “Show hidden icons”.", "Abre ao clicar no ícone em “Mostrar ícones ocultos”."),
            ["CornerTL"] = ("Top-left corner", "Canto superior esquerdo"),
            ["CornerTR"] = ("Top-right corner", "Canto superior direito"),
            ["CornerBL"] = ("Bottom-left corner", "Canto inferior esquerdo"),
            ["CornerBR"] = ("Bottom-right corner", "Canto inferior direito"),

            // ---- Quick menu ----
            ["ViewProcesses"] = ("View processes", "Ver processos"),
            ["LayoutTitle"] = ("LAYOUT", "LAYOUT"),
            ["LayoutCompact"] = ("Compact", "Compacto"),
            ["LayoutFull"] = ("Full", "Completo"),
            ["LayoutStrip"] = ("Strip", "Faixa"),
            ["ModeTitle"] = ("MODE", "MODO"),
            ["PositionTitle"] = ("POSITION", "POSIÇÃO"),
            ["PinnedToCorner"] = ("Pinned to corner", "Preso ao canto"),
            ["FreePositionDragged"] = ("Free position (dragged)", "Posição livre (arrastada)"),
            ["DragHint"] = ("Drag the widget to move it. Near a corner, it snaps.", "Arraste o widget para mover. Perto de um canto, ele encaixa."),
            ["LockPosition"] = ("Lock position", "Travar posição"),

            // ---- Tray ----
            ["PinToCorner"] = ("Pin to corner", "Fixar no canto"),
            ["TrayShowHide"] = ("Show / hide widget", "Mostrar / ocultar widget"),
            ["TrayResetPosition"] = ("Move widget back to its corner", "Voltar o widget para o canto"),
            ["Layout"] = ("Layout", "Layout"),
            ["Mode"] = ("Mode", "Modo"),
            ["LayoutCompactV"] = ("Compact vertical", "Compacto vertical"),
            ["LayoutExpanded"] = ("Expanded", "Expandido"),
            ["LayoutStripLong"] = ("Horizontal strip", "Faixa horizontal"),

            // ---- Processes ----
            ["ProcessesTitle"] = ("Processes", "Processos"),
            ["TopCpu"] = ("Processes using the most CPU", "Processos que mais usam CPU"),
            ["TopMemory"] = ("Processes using the most memory", "Processos que mais usam memória"),
            ["TopDisk"] = ("Processes using the most disk", "Processos que mais usam disco"),
            ["Top10"] = ("Top 10 · sorted by usage", "Top 10 · ordenado por uso"),
            ["ColProcess"] = ("PROCESS", "PROCESSO"),
            ["ColCpu"] = ("% CPU", "% CPU"),
            ["ColMemory"] = ("MEMORY", "MEMÓRIA"),
            ["ColDisk"] = ("DISK", "DISCO"),
            ["EndTask"] = ("End task", "Finalizar tarefa"),
            ["End"] = ("End", "Finalizar"),
            ["ConfirmPre"] = ("End", "Finalizar"),
            ["ConfirmPost"] = ("? Unsaved data in this app will be lost.", "? Dados não salvos nesse app serão perdidos."),
            ["Selected"] = ("Selected: {0} (PID {1})", "Selecionado: {0} (PID {1})"),
            ["SelectToEnd"] = ("Select a process to end it", "Selecione um processo para finalizar"),
            ["ProcCount"] = ("{0} processes", "{0} processos"),
            ["SumCpu"] = ("Total usage {0} · {1} processes", "Uso total {0} · {1} processos"),
            ["SumMemory"] = ("In use {0} of {1} GB · {2}", "Em uso {0} de {1} GB · {2}"),
            ["SumDisk"] = ("Activity {0} · R {1} MB/s · W {2} MB/s", "Atividade {0} · L {1} MB/s · E {2} MB/s"),
            ["KillSelf"] = ("To close PulseDock, use Quit in the menu.", "Para fechar o PulseDock, use Sair no menu."),
            ["KillCritical"] = ("{0} is an essential Windows process and can't be ended.", "{0} é um processo essencial do Windows e não pode ser finalizado."),
            ["KillOk"] = ("{0} was ended.", "{0} foi finalizado."),
            ["KillGone"] = ("{0} was no longer running.", "{0} já não estava em execução."),
            ["KillDenied"] = ("Access denied ending {0}. Restart PulseDock as administrator.", "Acesso negado ao finalizar {0}. Reinicie o PulseDock como administrador."),
            ["KillFailed"] = ("Couldn't end {0}: {1}", "Não foi possível finalizar {0}: {1}"),
            ["SysKernel"] = ("Windows kernel", "Kernel do Windows"),
            ["SysRegistry"] = ("Windows registry", "Registro do Windows"),
            ["SysMemCompression"] = ("Memory compression", "Compressão de memória"),
            ["SysProcess"] = ("System process", "Processo do sistema"),

            // ---- Settings ----
            ["DisplayMode"] = ("DISPLAY MODE", "MODO DE EXIBIÇÃO"),
            ["ScreenCorner"] = ("SCREEN CORNER", "CANTO DA TELA"),
            ["EdgeDistance"] = ("Distance from edge", "Distância da borda"),
            ["MonitorTitle"] = ("MONITOR", "MONITOR"),
            ["MonitorN"] = ("Monitor {0}", "Monitor {0}"),
            ["Primary"] = (" (primary)", " (principal)"),
            ["VisibleMetrics"] = ("VISIBLE METRICS", "MÉTRICAS VISÍVEIS"),
            ["MonitoredDisk"] = ("MONITORED DISK", "DISCO MONITORADO"),
            ["Behavior"] = ("BEHAVIOR", "COMPORTAMENTO"),
            ["BgOpacity"] = ("Widget opacity", "Opacidade do widget"),
            ["RefreshEvery"] = ("Refresh every", "Atualizar a cada"),
            ["HalfSecond"] = ("0.5 s", "0,5 s"),
            ["StartWithWindows"] = ("Start with Windows", "Iniciar com o Windows"),
            ["StartWithWindowsDesc"] = ("Opens automatically in the chosen mode.", "Abre automaticamente no modo escolhido."),
            ["ClickThrough"] = ("Click-through widget", "Clique atravessa o widget"),
            ["ClickThroughDesc"] = ("In overlay mode, clicks go to the window behind it. Hold Ctrl to interact.", "No modo sobreposto, cliques passam para a janela de trás. Segure Ctrl para interagir."),
            ["HideFullscreen"] = ("Hide in full screen", "Ocultar em tela cheia"),
            ["HideFullscreenDesc"] = ("Hides during full-screen games, videos and presentations.", "Some durante jogos, vídeos e apresentações em tela cheia."),
            ["AdvancedSensors"] = ("ADVANCED SENSORS", "SENSORES AVANÇADOS"),
            ["CpuDiskTemp"] = ("CPU and disk temperature", "Temperatura da CPU e do disco"),
            ["CpuDiskTempDesc"] = ("Uses the WinRing0 driver, which Microsoft Defender often flags as “HackTool”. Only works when running as administrator. When off, no driver is used (GPUs still show temperature).",
                                   "Usa o driver WinRing0, que o Microsoft Defender costuma marcar como “HackTool”. Só funciona executando como administrador. Desligado, nenhum driver é usado (as GPUs continuam com temperatura)."),
            ["RestartAdmin"] = ("Restart as admin", "Reiniciar como admin"),
            ["NotAdminDesc"] = ("Running without administrator rights. Required for advanced sensors and for ending protected processes.", "Executando sem administrador. Necessário para os sensores avançados e para finalizar processos protegidos."),
            ["LanguageTitle"] = ("LANGUAGE", "IDIOMA"),
            ["LangAuto"] = ("Automatic (Windows)", "Automático (Windows)"),

            // ---- Shortcuts ----
            ["ShortcutDesc"] = ("PulseDock: CPU, memory, disk, GPU and network monitor", "PulseDock: monitor de CPU, memória, disco, GPU e rede"),
        };
    }
}
