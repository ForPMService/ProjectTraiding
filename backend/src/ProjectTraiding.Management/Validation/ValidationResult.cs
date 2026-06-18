using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.Validation
{
    public sealed class ValidationResult
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }
}
