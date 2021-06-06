namespace WalkerSim
{
    public static class Logger
    {
        public static void Info(string message, params object[] objs)
        {
            Log.Out($"[WalkerSim]{message}", objs);
        }
        
        public static void Warning(string message, params object[] objs)
        {
            Log.Warning($"[WalkerSim]{message}", objs);
        }
        
        public static void Error(string message, params object[] objs)
        {
            Log.Error($"[WalkerSim]{message}", objs);
        }
        
        public static void Debug(string message)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}");
#endif
        }
        
        public static void Debug(string message, object o1)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}", new[] { o1 });
#endif
        }
        
        public static void Debug(string message, object o1, object o2)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}", new[] { o1, o2 });
#endif
        }
        
        public static void Debug(string message, object o1, object o2, object o3)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}", new[] { o1, o2, o3 });
#endif
        }
        
        public static void Debug(string message, object o1, object o2, object o3, object o4)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}", new[] { o1, o2, o3, o4 });
#endif
        }
        
        public static void Debug(string message, object o1, object o2, object o3, object o4, object o5)
        {
#if DEBUG
            Log.Out($"[WalkerSim]{message}", new[] { o1, o2, o3, o4, o5 });
#endif
        }
    }
}