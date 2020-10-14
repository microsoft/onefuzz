$exe = "setup.exe"
$url = "https://cygwin.com/setup-x86_64.exe"
$mirror = "http://cygwin.mirror.constant.com"
$dest = "c:\cygwin"
Invoke-WebRequest $url -OutFile $exe
Start-Process -wait -FilePath $exe -ArgumentList "-q -n -s $mirror -R $dest"
Start-Process -wait -FilePath $exe -ArgumentList "-q -n -s $mirror -R $dest -P _autorebase,alternatives,base-cygwin,base-files,bash,binutils,bzip2,ca-certificates,coreutils,`
crypto-policies,cygutils,cygwin,cygwin-devel,dash,diffutils,editrights,file,findutils,gawk,`
gcc-core,gcc-g++,getent,grep,groff,gzip,hostname,info,ipc-utils,less,libargp,libatomic1,libattr1,`
libblkid1,libbz2_1,libcrypt2,libfdisk1,libffi6,libgc1,libgcc1,libgdbm4,libgmp10,libgnutls30,libgomp1,`
libguile2.2_1,libhogweed4,libiconv,libiconv2,libidn2_0,libintl8,libisl22,libltdl7,liblzma5,libmpc3,`
libmpfr6,libncursesw10,libnettle6,libp11-kit0,libpcre1,libpipeline1,libpopt-common,libpopt0,libpsl5,`
libquadmath0,libreadline7,libsigsegv2,libsmartcols1,libssl1.1,libstdc++6,libtasn1_6,libunistring2,`
libuuid1,login,make,man-db,mintty,ncurses,openssl,p11-kit,p11-kit-trust,publicsuffix-list-dafsa,`
rebase,run,sed,tar,terminfo,terminfo-extra,tzcode,tzdata,util-linux,vim-minimal,w32api-headers,`
w32api-runtime,wget,which,windows-default-manifest,xz,zlib0"
# git clone https://gitlab.com/akihe/radamsa
# cd radamsa
# git checkout 8121b78fb8f87e869cbeca931964df2b32435eb7
# c:\cygwin\bin\bash.exe -c "make --debug=v"
# copy $installRoot\bin\cygwin1.dll bin\cygwin1.dll