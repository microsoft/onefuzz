// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{format_err, Result};

#[derive(Clone, Copy, Debug)]
pub struct ItaniumDemangler {
    options: cpp_demangle::DemangleOptions,
}

impl ItaniumDemangler {
    pub fn try_demangle(&self, raw: impl AsRef<str>) -> Result<String> {
        let symbol = cpp_demangle::Symbol::new(raw.as_ref())?;
        Ok(symbol.demangle(&self.options)?)
    }
}

impl Default for ItaniumDemangler {
    fn default() -> Self {
        let options = cpp_demangle::DemangleOptions::new()
            .no_params()
            .no_return_type();
        Self { options }
    }
}

#[derive(Clone, Copy, Debug)]
pub struct MsvcDemangler {
    flags: msvc_demangler::DemangleFlags,
}

impl MsvcDemangler {
    pub fn try_demangle(&self, raw: impl AsRef<str>) -> Result<String> {
        Ok(msvc_demangler::demangle(raw.as_ref(), self.flags)?)
    }
}

impl Default for MsvcDemangler {
    fn default() -> Self {
        // Equivalent to `undname 0x1000`.
        let flags = msvc_demangler::DemangleFlags::NAME_ONLY;
        Self { flags }
    }
}

#[derive(Clone, Copy, Debug, Default)]
pub struct RustcDemangler;

impl RustcDemangler {
    pub fn try_demangle(&self, raw: impl AsRef<str>) -> Result<String> {
        let name = rustc_demangle::try_demangle(raw.as_ref())
            .map_err(|_| format_err!("unable to demangle rustc name"))?;

        // Alternate formatter discards trailing hash.
        Ok(format!("{:#}", name))
    }
}

/// Demangler that tries to demangle a raw name against each known scheme.
#[derive(Clone, Copy, Debug, Default)]
pub struct Demangler {
    itanium: ItaniumDemangler,
    msvc: MsvcDemangler,
    rustc: RustcDemangler,
}

impl Demangler {
    /// Try to demangle a raw name according to a set of known schemes.
    ///
    /// The following schemes are tried in-order:
    ///   1. rustc
    ///   2. Itanium
    ///   3. MSVC
    ///
    /// The first scheme to provide some demangling is used. If the name does
    /// not parse against any of the known schemes, return `None`.
    pub fn demangle(&self, raw: impl AsRef<str>) -> Option<String> {
        let raw = raw.as_ref();

        // Try `rustc` demangling first.
        //
        // Ensures that if a name _also_ demangles against the Itanium scheme,
        // we are sure to remove the hash suffix from the demangled name.
        if let Ok(demangled) = self.rustc.try_demangle(raw) {
            return Some(demangled);
        }

        if let Ok(demangled) = self.itanium.try_demangle(raw) {
            return Some(demangled);
        }

        if let Ok(demangled) = self.msvc.try_demangle(raw) {
            return Some(demangled);
        }

        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_demangler_itanium_llvm() {
        let test_cases = &[
            (
                "_ZN11__sanitizer20SizeClassAllocator64IN6__asan4AP64INS_21LocalAddressSpaceViewEEEE21ReleaseFreeMemoryToOSINS5_12MemoryMapperEEEvPjmmmPT_",
                "__sanitizer::SizeClassAllocator64<__asan::AP64<__sanitizer::LocalAddressSpaceView> >::ReleaseFreeMemoryToOS<__sanitizer::SizeClassAllocator64<__asan::AP64<__sanitizer::LocalAddressSpaceView> >::MemoryMapper>",
            ),
            (
                "_ZN11__sanitizer14ThreadRegistry23FindThreadContextLockedEPFbPNS_17ThreadContextBaseEPvES3_",
                "__sanitizer::ThreadRegistry::FindThreadContextLocked",
            ),
            (
                "_ZN7Greeter5GreetEi",
                "Greeter::Greet",
            ),
            (
                "_ZN7Greeter5GreetEv",
                "Greeter::Greet",
            ),
            (
                "_ZN7Greeter5GreetERNSt7__cxx1112basic_stringIcSt11char_traitsIcESaIcEEE",
                "Greeter::Greet",
            ),
            (
                "_ZN7NothingIPvE3NopES0_",
                "Nothing<void*>::Nop",
            ),
            (
                "_ZN7NothingIiE3NopEi",
                "Nothing<int>::Nop",
            ),
            (
                "_ZN7NothingIRdE3NopES0_",
                "Nothing<double&>::Nop",
            ),
        ];

        let demangler = Demangler::default();

        for (mangled, demangled) in test_cases {
            let name = demangler
                .demangle(mangled)
                .unwrap_or_else(|| panic!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }

        assert!(demangler.demangle("main").is_none());
        assert!(demangler.demangle("_some_function").is_none());
    }

    #[test]
    fn test_demangler_msvc() {
        let test_cases = &[
            (
                "?Greet@Greeter@@QEAAXXZ",
                "Greeter::Greet",
            ),
            (
                "?Greet@Greeter@@QEAAXH@Z",
                "Greeter::Greet",
            ),
            (
                "?Greet@Greeter@@QEAAXAEAV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@@Z",
                "Greeter::Greet",
            ),
            (
                "?Nop@?$Nothing@H@@QEAAXH@Z",
                "Nothing<int>::Nop",
            ),
            (
                "?Nop@?$Nothing@AEAN@@QEAAXAEAN@Z",
                "Nothing<double &>::Nop",
            ),
            (
                "?Nop@?$Nothing@PEAX@@QEAAXPEAX@Z",
                "Nothing<void *>::Nop",
            ),
        ];

        let demangler = Demangler::default();

        for (mangled, demangled) in test_cases {
            let name = demangler
                .demangle(mangled)
                .unwrap_or_else(|| panic!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }

        assert!(demangler.demangle("main").is_none());
        assert!(demangler.demangle("_some_function").is_none());
    }

    #[test]
    fn test_demangler_rustc() {
        let test_cases = &[
            (
                "_ZN3std2io5stdio9set_panic17hcf1e5c38cefca0deE",
                "std::io::stdio::set_panic",
            ),
            (
                "_ZN4core3fmt3num53_$LT$impl$u20$core..fmt..LowerHex$u20$for$u20$i64$GT$3fmt17h7ebe6c0818892343E",
                "core::fmt::num::<impl core::fmt::LowerHex for i64>::fmt",
            ),
        ];

        let demangler = Demangler::default();

        for (mangled, demangled) in test_cases {
            let name = demangler
                .demangle(mangled)
                .unwrap_or_else(|| panic!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }

        assert!(demangler.demangle("main").is_none());
        assert!(demangler.demangle("_some_function").is_none());
    }
}
