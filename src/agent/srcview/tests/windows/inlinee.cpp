#include <iostream>

__declspec(dllexport) void test();

int main()
{
    test();
}

__declspec(dllexport) void test() {
    std::cout << "Hello World!\n";
}
