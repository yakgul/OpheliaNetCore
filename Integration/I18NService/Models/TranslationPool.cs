﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Ophelia.Integration.I18NService.Models
{
    public class TranslationPool: IDisposable
    {
        public string Name { get; set; }

        public virtual ICollection<TranslationPool_i18n> TranslationPool_i18n { get; set; }

        public void Dispose()
        {
            this.Name = "";
            this.TranslationPool_i18n = null;
        }
    }
}
