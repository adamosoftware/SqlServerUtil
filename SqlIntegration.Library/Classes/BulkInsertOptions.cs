﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Postulate.Integration.SqlServer.Classes
{
    public class BulkInsertOptions
    {
        public bool TruncateFirst { get; set; }
        public string SkipIdentityColumn { get; set; }
        public Func<SqlConnection, DataRow, Task<bool>> IncludeRowCallback { get; set; }
    }
}