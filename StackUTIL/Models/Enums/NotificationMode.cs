using System;
using System.Collections.Generic;
using System.Text;

namespace StackUTIL.Models.Enums
{
    public enum NotificationMode
    {
        /// <summary>Показывать MessageBox (UI)</summary>
        MessageBox = 0,
        /// <summary>Только логирование (без модальных окон)</summary>
        LogOnly = 1,
        /// <summary>И лог, и MessageBox</summary>
        Both = 2
    }
}
