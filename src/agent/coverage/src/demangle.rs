// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{format_err, Result};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum Demangler {
    /// Itanium C++ ABI mangling
    Itanium,

    /// MSVC decorated names
    Msvc,

    /// Rustc name mangling
    Rustc,
}

impl Demangler {
    pub fn demangle(&self, raw: impl AsRef<str>) -> Result<String> {
        let raw = raw.as_ref();

        let demangled = match self {
            Demangler::Itanium => {
                use cpp_demangle::{DemangleOptions, Symbol};

                let options = DemangleOptions::new()
                    .no_params()
                    .no_return_type();
                let symbol = Symbol::new(raw)?;
                symbol.demangle(&options)?
            },
            Demangler::Msvc => {
                let flags = msvc_demangler::DemangleFlags::NAME_ONLY;
                msvc_demangler::demangle(raw, flags)?
            }
            Demangler::Rustc => {
                let name = rustc_demangle::try_demangle(raw)
                    .map_err(|_| format_err!("unable to demangle rustc name"))?;

                // Alternate formatter discards trailing hash.
                format!("{:#}", name)
            },
        };

        Ok(demangled)
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

        let d = Demangler::Itanium;

        for (mangled, demangled) in test_cases {
            let name = d.demangle(mangled).expect(&format!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }
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

        let d = Demangler::Msvc;

        for (mangled, demangled) in test_cases {
            let name = d.demangle(mangled).expect(&format!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }
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

        let d = Demangler::Rustc;

        for (mangled, demangled) in test_cases {
            let name = d.demangle(mangled).expect(&format!("demangling error: {}", mangled));
            assert_eq!(&name, demangled);
        }
    }
}
