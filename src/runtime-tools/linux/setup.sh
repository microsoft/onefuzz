#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


find .
set -x

INSTANCE_OS_SETUP="/onefuzz/instance-specific-setup/linux/setup.sh"
INSTANCE_SETUP="/onefuzz/instance-specific-setup/setup.sh"
USER_SETUP="/onefuzz/setup/setup.sh"
TASK_SETUP="/onefuzz/bin/task-setup.sh"
MANAGED_SETUP="/onefuzz/bin/managed.sh"
SCALESET_SETUP="/onefuzz/bin/scaleset-setup.sh"
DOTNET_VERSIONS=('7.0')
export DOTNET_ROOT=/onefuzz/tools/dotnet
export DOTNET_CLI_HOME="$DOTNET_ROOT"
export ONEFUZZ_ROOT=/onefuzz
export LLVM_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer

logger "onefuzz: making directories"
sudo mkdir -p /onefuzz/downloaded
sudo chown -R $(whoami) /onefuzz
mv * /onefuzz/downloaded
cd /onefuzz
mkdir -p /onefuzz/bin
mkdir -p /onefuzz/logs
mkdir -p /onefuzz/setup
mkdir -p /onefuzz/tools
mkdir -p /onefuzz/etc
mkdir -p /onefuzz/instance-specific-setup
mkdir -p "$DOTNET_ROOT"

echo $1 > /onefuzz/etc/mode
export PATH=$PATH:/onefuzz/bin:/onefuzz/tools/linux:/onefuzz/tools/linux/afl:/onefuzz/tools/linux/radamsa

# Basic setup
mv /onefuzz/downloaded/config.json /onefuzz
mv /onefuzz/downloaded/azcopy /onefuzz/bin
mv /onefuzz/downloaded/managed.sh /onefuzz/bin

if [ -f /onefuzz/downloaded/task-setup.sh ]; then
    mv /onefuzz/downloaded/task-setup.sh /onefuzz/bin/
fi

if [ -f /onefuzz/downloaded/repro.sh ]; then
    mv /onefuzz/downloaded/repro.sh /onefuzz/bin/
fi
if [ -f /onefuzz/downloaded/repro-stdout.sh ]; then
    mv /onefuzz/downloaded/repro-stdout.sh /onefuzz/bin/
fi
if [ -f /onefuzz/downloaded/scaleset-setup.sh ]; then
    mv /onefuzz/downloaded/scaleset-setup.sh /onefuzz/bin
fi

chmod -R a+rx /onefuzz/bin

if [ -f ${MANAGED_SETUP} ]; then
    logger "onefuzz: managed setup script start"
    chmod +x ${MANAGED_SETUP}
    ${MANAGED_SETUP} 2>&1 | logger -s -i -t 'onefuzz-managed-setup'
    logger "onefuzz: managed setup script stop"
else
    logger "onefuzz: no managed setup script"
fi

if [ -f ${SCALESET_SETUP} ]; then
    logger "onefuzz: scaleset setup script start"
    chmod +x ${SCALESET_SETUP}
    ${SCALESET_SETUP} 2>&1 | logger -s -i -t 'onefuzz-scaleset-setup'
    logger "onefuzz: scaleset setup script stop"
else
    logger "onefuzz: no scaleset setup script"
fi

if [ -f ${INSTANCE_SETUP} ]; then
    logger "onefuzz: instance setup script start"
    chmod +x ${INSTANCE_SETUP}
    ${INSTANCE_SETUP} 2>&1 | logger -s -i -t 'onefuzz-instance-setup'
    logger "onefuzz: instance setup script stop"
elif [ -f ${INSTANCE_OS_SETUP} ]; then
    logger "onefuzz: instance setup script (linux) start"
    chmod +x ${INSTANCE_OS_SETUP}
    ${INSTANCE_OS_SETUP} 2>&1 | logger -s -i -t 'onefuzz-instance-setup'
    logger "onefuzz: instance setup script stop"
else
    logger "onefuzz: no instance setup script"
fi

# When repro case is moved into the supervisor, this should be deleted
if [ -f ${TASK_SETUP} ]; then
    logger "onefuzz: task-specific setup script start"
    chmod +x ${TASK_SETUP}
    ${TASK_SETUP} 2>&1 | logger -s -i -t 'onefuzz-task-setup'
    logger "onefuzz: task-specific setup script stop"
else
    logger "onefuzz: no task-specific setup script"
fi


if [ -f ${USER_SETUP} ]; then
    logger "onefuzz: user-specific setup script start"
    chmod +x ${USER_SETUP}
    ${USER_SETUP} 2>&1 | logger -s -i -t 'onefuzz-user-setup'
    logger "onefuzz: user-specific setup script stop"
else
    logger "onefuzz: no user-specific setup script"
fi

chmod -R a+rx /onefuzz/tools/linux

if type apt > /dev/null 2> /dev/null; then

    # Install updated Microsoft Open Management Infrastructure - github.com/microsoft/omi
    curl -sSL https://packages.microsoft.com/keys/microsoft.asc | sudo tee /etc/apt/trusted.gpg.d/microsoft.asc 2>&1 | logger -s -i -t 'onefuzz-OMI-add-MS-repo-key'
    sudo apt-add-repository https://packages.microsoft.com/ubuntu/20.04/prod 2>&1 | logger -s -i -t 'onefuzz-OMI-add-MS-repo'
    sudo apt update
    sleep 10
    sudo apt-get install -y omi=1.6.10.2 2>&1 | logger -s -i -t 'onefuzz-OMI-install'

    until sudo apt install -y gdb gdbserver; do
        echo "apt failed.  sleep 10s, then retrying"
        sleep 10
    done

    if ! [ -f ${LLVM_SYMBOLIZER_PATH} ]; then
        until sudo apt install -y llvm-12; do
            echo "apt failed, sleeping 10s then retrying"
            sleep 10
        done

        # If specifying symbolizer, exe name must be a "known symbolizer".
        # Using `llvm-symbolizer` works for clang 8 .. 12.
        sudo ln -f -s $(which llvm-symbolizer-12) $LLVM_SYMBOLIZER_PATH
    fi

    # Install dotnet
    until sudo apt install -y curl libicu-dev; do
        logger "apt failed, sleeping 10s then retrying"
        sleep 10
    done

    logger "downloading dotnet install"
    curl --retry 10 -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh 2>&1 | logger -s -i -t 'onefuzz-curl-dotnet-install'
    chmod +x dotnet-install.sh

    for version in "${DOTNET_VERSIONS[@]}"; do
        logger "running dotnet install $version"
        /bin/bash ./dotnet-install.sh --channel "$version" --install-dir "$DOTNET_ROOT" 2>&1 | logger -s -i -t 'onefuzz-dotnet-setup'
    done
    rm dotnet-install.sh

    logger "install dotnet tools"
    pushd "$DOTNET_ROOT"
    ls -lah 2>&1 | logger -s -i -t 'onefuzz-dotnet-tools'
    "$DOTNET_ROOT"/dotnet tool install dotnet-dump --version 6.0.351802 --tool-path /onefuzz/tools 2>&1 | logger -s -i -t 'onefuzz-dotnet-tools'
    "$DOTNET_ROOT"/dotnet tool install dotnet-coverage --version 17.5 --tool-path /onefuzz/tools 2>&1 | logger -s -i -t 'onefuzz-dotnet-tools'
    "$DOTNET_ROOT"/dotnet tool install dotnet-sos --version 6.0.351802 --tool-path /onefuzz/tools 2>&1 | logger -s -i -t 'onefuzz-dotnet-tools'
    popd
fi

if  [ -v DOCKER_BUILD ]; then
    echo "building for docker"
elif [ -d /etc/systemd/system ]; then
    logger "onefuzz: setting up systemd"
    sudo chmod 644 /onefuzz/tools/linux/onefuzz.service
    sudo chown root /onefuzz/tools/linux/onefuzz.service
    sudo ln -s /onefuzz/tools/linux/onefuzz.service /etc/systemd/system/onefuzz.service
    sudo systemctl enable onefuzz
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: restarting"
        echo rebooting
        sudo reboot
    else
        logger "onefuzz: starting via systemd"
        sudo systemctl start onefuzz
    fi
elif [ -d /etc/init.d ]; then
    logger "onefuzz: setting up init.d"
    sudo chown root /onefuzz/tools/linux/onefuzz.initd
    sudo ln -s /onefuzz/tools/linux/onefuzz.initd /etc/init.d/onefuzz
    RCDIRS=/etc/rc2.d /etc/rc3.d /etc/rc4.d /etc/rc5.d
    for RCDIR in ${RCDRS}; do
        if [ -d ${RCDIR} ]; then
            sudo ln -s /onefuzz/tools/linux/onefuzz.initd ${RCDIR}/S99onefuzz
        fi
    done
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: rebooting"
        sudo reboot
    else
        logger "onefuzz: starting via init"
        sudo /etc/init.d/onefuzz start
    fi
else
    logger "onefuzz: unknown startup"
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: rebooting without startup script"
        sudo reboot
    else
        logger "onefuzz: starting directly"
        nohup sudo /onefuzz/tools/linux/run.sh
    fi
fi
