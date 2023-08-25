// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::io::{Cursor, Write};

use quick_xml::{Result, Writer};

impl CoberturaCoverage {
    pub fn to_string(&self) -> anyhow::Result<String> {
        let mut data = Vec::new();
        let cursor = Cursor::new(&mut data);

        let mut writer = Writer::new_with_indent(cursor, b' ', 2);

        self._write_xml(&mut writer)?;

        let text = String::from_utf8(data)?;
        Ok(text)
    }
}

pub trait WriteXml {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()>;

    fn write_xml<W: Write>(&self, writer: W) -> Result<()> {
        let mut writer = Writer::new(writer);
        self._write_xml(&mut writer)
    }
}

// Only write optional fields if present.
impl<T> WriteXml for Option<T>
where
    T: WriteXml,
{
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        if let Some(value) = self {
            value._write_xml(writer)?;
        }

        Ok(())
    }
}

impl<T> WriteXml for Vec<T>
where
    T: WriteXml,
{
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        for value in self {
            value._write_xml(writer)?;
        }

        Ok(())
    }
}

macro_rules! float {
    ($val: expr) => {
        format!("{:.02}", $val).as_str()
    };
}

macro_rules! uint {
    ($val: expr) => {
        format!("{}", $val).as_str()
    };
}

macro_rules! boolean {
    ($val: expr) => {
        format!("{}", $val).as_str()
    };
}

macro_rules! string {
    ($val: expr) => {
        &*quick_xml::escape::escape(&$val)
    };
}

// <!ELEMENT coverage (sources?,packages)>
// <!ATTLIST coverage line-rate        CDATA #REQUIRED>
// <!ATTLIST coverage branch-rate      CDATA #REQUIRED>
// <!ATTLIST coverage lines-covered    CDATA #REQUIRED>
// <!ATTLIST coverage lines-valid      CDATA #REQUIRED>
// <!ATTLIST coverage branches-covered CDATA #REQUIRED>
// <!ATTLIST coverage branches-valid   CDATA #REQUIRED>
// <!ATTLIST coverage complexity       CDATA #REQUIRED>
// <!ATTLIST coverage version          CDATA #REQUIRED>
// <!ATTLIST coverage timestamp        CDATA #REQUIRED>
#[derive(Clone, Debug, Default)]
pub struct CoberturaCoverage {
    pub sources: Option<Sources>,
    pub packages: Packages,

    pub line_rate: f64,
    pub branch_rate: f64,
    pub lines_covered: u64,
    pub lines_valid: u64,
    pub branches_covered: u64,
    pub branches_valid: u64,
    pub complexity: u64,
    pub version: String,
    pub timestamp: u64,
}

impl WriteXml for CoberturaCoverage {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("coverage")
            .with_attributes([
                ("line-rate", float!(self.line_rate)),
                ("branch-rate", float!(self.branch_rate)),
                ("lines-covered", uint!(self.lines_covered)),
                ("lines-valid", uint!(self.lines_valid)),
                ("branches-covered", uint!(self.branches_covered)),
                ("branches-valid", uint!(self.branches_valid)),
                ("complexity", uint!(self.complexity)),
                ("version", string!(self.version)),
                ("timestamp", uint!(self.timestamp)),
            ])
            .write_inner_content(|w| {
                self.sources._write_xml(w)?;
                self.packages._write_xml(w)?;

                Ok(())
            })?;

        Ok(())
    }
}

// <!ELEMENT sources (source*)>
#[derive(Clone, Debug, Default)]
pub struct Sources {
    pub sources: Vec<Source>,
}

impl WriteXml for Sources {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("sources")
            .write_inner_content(|w| self.sources._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT source (#PCDATA)>
#[derive(Clone, Debug, Default)]
pub struct Source {
    pub path: String,
}

impl WriteXml for Source {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("source")
            .with_attributes([("path", string!(self.path))])
            .write_empty()?;

        Ok(())
    }
}

// <!ELEMENT packages (package*)>
#[derive(Clone, Debug, Default)]
pub struct Packages {
    pub packages: Vec<Package>,
}

impl WriteXml for Packages {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("packages")
            .write_inner_content(|w| self.packages._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT package (classes)>
// <!ATTLIST package name        CDATA #REQUIRED>
// <!ATTLIST package line-rate   CDATA #REQUIRED>
// <!ATTLIST package branch-rate CDATA #REQUIRED>
// <!ATTLIST package complexity  CDATA #REQUIRED>
#[derive(Clone, Debug, Default)]
pub struct Package {
    pub classes: Classes,

    pub name: String,
    pub line_rate: f64,
    pub branch_rate: f64,
    pub complexity: u64,
}

impl WriteXml for Package {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("package")
            .with_attributes([
                ("name", string!(self.name)),
                ("line-rate", float!(self.line_rate)),
                ("branch-rate", float!(self.branch_rate)),
                ("complexity", uint!(self.complexity)),
            ])
            .write_inner_content(|w| self.classes._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT classes (class*)>
#[derive(Clone, Debug, Default)]
pub struct Classes {
    pub classes: Vec<Class>,
}

impl WriteXml for Classes {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("classes")
            .write_inner_content(|w| self.classes._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT class (methods,lines)>
// <!ATTLIST class name        CDATA #REQUIRED>
// <!ATTLIST class filename    CDATA #REQUIRED>
// <!ATTLIST class line-rate   CDATA #REQUIRED>
// <!ATTLIST class branch-rate CDATA #REQUIRED>
// <!ATTLIST class complexity  CDATA #REQUIRED>
#[derive(Clone, Debug, Default)]
pub struct Class {
    pub methods: Methods,
    pub lines: Lines,

    pub name: String,
    pub filename: String,
    pub line_rate: f64,
    pub branch_rate: f64,
    pub complexity: u64,
}

impl WriteXml for Class {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("class")
            .with_attributes([
                ("name", string!(self.name)),
                ("filename", string!(self.filename)),
                ("line-rate", float!(self.line_rate)),
                ("branch-rate", float!(self.branch_rate)),
                ("complexity", uint!(self.complexity)),
            ])
            .write_inner_content(|w| {
                self.methods._write_xml(w)?;
                self.lines._write_xml(w)?;
                Ok(())
            })?;

        Ok(())
    }
}

// <!ELEMENT methods (method*)>
#[derive(Clone, Debug, Default)]
pub struct Methods {
    pub methods: Vec<Method>,
}

impl WriteXml for Methods {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("methods")
            .write_inner_content(|w| self.methods._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT method (lines)>
// <!ATTLIST method name        CDATA #REQUIRED>
// <!ATTLIST method signature   CDATA #REQUIRED>
// <!ATTLIST method line-rate   CDATA #REQUIRED>
// <!ATTLIST method branch-rate CDATA #REQUIRED>
#[derive(Clone, Debug, Default)]
pub struct Method {
    pub lines: Lines,

    pub name: String,
    pub signature: String,
    pub line_rate: f64,
    pub branch_rate: f64,
}

impl WriteXml for Method {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("method")
            .with_attributes([
                ("name", string!(self.name)),
                ("signature", string!(self.signature)),
                ("line-rate", float!(self.line_rate)),
                ("branch-rate", float!(self.branch_rate)),
            ])
            .write_inner_content(|w| self.lines._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT lines (line*)>
#[derive(Clone, Debug, Default)]
pub struct Lines {
    pub lines: Vec<Line>,
}

impl WriteXml for Lines {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("lines")
            .write_inner_content(|w| self.lines._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT line (conditions*)>
// <!ATTLIST line number CDATA #REQUIRED>
// <!ATTLIST line hits   CDATA #REQUIRED>
// <!ATTLIST line branch CDATA "false">
// <!ATTLIST line condition-coverage CDATA "100%">
#[derive(Clone, Debug, Default)]
pub struct Line {
    pub conditions: Conditions,

    pub number: u64,
    pub hits: u64,
    pub branch: Option<bool>,
    pub condition_coverage: Option<String>,
}

impl WriteXml for Line {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        let condition_coverage = if let Some(s) = &self.condition_coverage {
            s.as_str()
        } else {
            "100%"
        };

        writer
            .create_element("line")
            .with_attributes([
                ("number", uint!(self.number)),
                ("hits", uint!(self.hits)),
                ("branch", boolean!(self.branch.unwrap_or_default())),
                ("condition-coverage", condition_coverage),
            ])
            .write_inner_content(|w| self.conditions._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT conditions (condition*)>
#[derive(Clone, Debug, Default)]
pub struct Conditions {
    pub conditions: Vec<Condition>,
}

impl WriteXml for Conditions {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("conditions")
            .write_inner_content(|w| self.conditions._write_xml(w))?;

        Ok(())
    }
}

// <!ELEMENT condition EMPTY>
// <!ATTLIST condition number CDATA #REQUIRED>
// <!ATTLIST condition type CDATA #REQUIRED>
// <!ATTLIST condition coverage CDATA #REQUIRED>
#[derive(Clone, Debug, Default)]
pub struct Condition {
    pub number: u64,
    pub r#type: u64,
    pub coverage: u64,
}

impl WriteXml for Condition {
    fn _write_xml<W: Write>(&self, writer: &mut Writer<W>) -> Result<()> {
        writer
            .create_element("condition")
            .with_attributes([
                ("number", uint!(self.number)),
                ("type", uint!(self.r#type)),
                ("coverage", uint!(self.coverage)),
            ])
            .write_empty()?;

        Ok(())
    }
}
